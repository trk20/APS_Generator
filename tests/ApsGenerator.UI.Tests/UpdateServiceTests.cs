using System.Text;
using System.Text.Json;
using ApsGenerator.UI.Services;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace ApsGenerator.UI.Tests;

[Trait("Category", "Unit")]
public sealed class UpdateServiceTests
{
    private const string PackageId = "APS-Generator";
    private const string DefaultChannel = "linux";
    private const string RepoUrl = "https://github.com/trk20/APS_Generator";
    private const string ReleaseTagUrlPrefix = "https://github.com/trk20/APS_Generator/releases/tag/v";
    private const string Sha1 = "0000000000000000000000000000000000000000";

    [Fact]
    public async Task CheckForUpdatesAsync_FindsNewerVersion_FromLocalFeed()
    {
        using var temp = new TempDirectoryScope();
        var locator = CreateLocator(version: "2.1.0", packagesDir: temp.DirectoryPath, channel: DefaultChannel);
        WriteFeed(temp.DirectoryPath, locator.Channel!, "2.2.0");

        var source = new SimpleFileSource(new DirectoryInfo(temp.DirectoryPath));
        var manager = UpdateService.CreateUpdateManager(source, locator);

        var updateInfo = await manager.CheckForUpdatesAsync();

        Assert.NotNull(updateInfo);
        Assert.Equal("2.2.0", updateInfo!.TargetFullRelease.Version.ToString());
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("1.0.0")]
    public async Task CheckForUpdatesAsync_ReturnsNull_WhenSameOrOlderWithoutDowngrade(string feedVersion)
    {
        using var temp = new TempDirectoryScope();
        var locator = CreateLocator(version: "2.0.0", packagesDir: temp.DirectoryPath, channel: DefaultChannel);
        WriteFeed(temp.DirectoryPath, locator.Channel!, feedVersion);

        var source = new SimpleFileSource(new DirectoryInfo(temp.DirectoryPath));
        var manager = new UpdateManager(source, new UpdateOptions { AllowVersionDowngrade = false }, locator);

        var updateInfo = await manager.CheckForUpdatesAsync();

        Assert.Null(updateInfo);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_FindsOlderVersion_WhenDowngradeAllowed()
    {
        using var temp = new TempDirectoryScope();
        var locator = CreateLocator(version: "2.0.0", packagesDir: temp.DirectoryPath, channel: DefaultChannel);
        WriteFeed(temp.DirectoryPath, locator.Channel!, "1.0.0");

        var source = new SimpleFileSource(new DirectoryInfo(temp.DirectoryPath));
        var manager = UpdateService.CreateUpdateManager(source, locator);

        var updateInfo = await manager.CheckForUpdatesAsync();

        Assert.NotNull(updateInfo);
        Assert.True(updateInfo!.IsDowngrade);
        Assert.Equal("1.0.0", updateInfo.TargetFullRelease.Version.ToString());
    }

    [Fact]
    public async Task CheckForUpdatesAsync_Throws_WhenFeedJsonIsMalformed()
    {
        using var temp = new TempDirectoryScope();
        var locator = CreateLocator(version: "1.0.0", packagesDir: temp.DirectoryPath, channel: DefaultChannel);
        var malformedFeedPath = Path.Combine(temp.DirectoryPath, $"releases.{locator.Channel}.json");
        File.WriteAllText(malformedFeedPath, "not-json");

        var source = new SimpleFileSource(new DirectoryInfo(temp.DirectoryPath));
        var manager = UpdateService.CreateUpdateManager(source, locator);

        await Assert.ThrowsAnyAsync<Exception>(() => manager.CheckForUpdatesAsync());
    }

    [Theory]
    [InlineData(false, "1.1.0")]
    [InlineData(true, "2.0.0")]
    public async Task GithubSource_PrereleaseFlag_ControlsVisibleReleases(bool receiveExperimental, string expectedVersion)
    {
        using var temp = new TempDirectoryScope();
        var locator = CreateLocator(version: "1.0.0", packagesDir: temp.DirectoryPath, channel: DefaultChannel);
        var downloader = FakeGithubDownloader.Create(channel: locator.Channel!);
        var source = new GithubSource(RepoUrl, accessToken: null, prerelease: receiveExperimental, downloader: downloader);
        var manager = UpdateService.CreateUpdateManager(source, locator);

        var updateInfo = await manager.CheckForUpdatesAsync();

        Assert.NotNull(updateInfo);
        Assert.Equal(expectedVersion, updateInfo!.TargetFullRelease.Version.ToString());
    }

    [Fact]
    public void GetCurrentVersion_ReturnsDevForDefaultVersion_ElseMajorMinorBuildFormat()
    {
        var version = UpdateService.GetCurrentVersion();

        Assert.NotNull(version);

        var asmVersion = typeof(UpdateService).Assembly.GetName().Version!;
        var expected = $"{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}";
        if (expected == "1.0.0")
        {
            expected = "dev";
        }

        Assert.Equal(expected, version);
    }

    [Fact]
    public void ProcessReleaseNotes_ReturnsEmpty_ForNullInput()
    {
        var result = UpdateService.ProcessReleaseNotes(null, "1.2.3");

        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void ProcessReleaseNotes_ReturnsEmpty_ForEmptyOrWhitespaceInput(string? markdown)
    {
        var result = UpdateService.ProcessReleaseNotes(markdown, "1.2.3");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ProcessReleaseNotes_PreservesMarkdownAndAppendsGithubLink_WhenNoMarkers()
    {
        var markdown = "## Highlights\n- Added feature";
        var version = "2.3.4";

        var result = UpdateService.ProcessReleaseNotes(markdown, version);

        var expected = $"{markdown}\n\n---\n[View this release on GitHub]({ReleaseTagUrlPrefix}{version})";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProcessReleaseNotes_StripsInstallMarkerSectionAndAppendsGithubLink()
    {
        var markdown = "## Notes\nIntro\n<!--   install-start   -->\nInstall steps that should be hidden.\n<!-- \tinstall-end\t -->\nOutro";
        var version = "3.0.0";

        var result = UpdateService.ProcessReleaseNotes(markdown, version);

        var expectedBody = "## Notes\nIntro\n\nOutro";
        var expected = $"{expectedBody}\n\n---\n[View this release on GitHub]({ReleaseTagUrlPrefix}{version})";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProcessReleaseNotes_UsesProvidedVersionInGithubLink()
    {
        var markdown = "Release details";
        var version = "9.1.0-beta.2";

        var result = UpdateService.ProcessReleaseNotes(markdown, version);

        Assert.Contains($"({ReleaseTagUrlPrefix}{version})", result);
    }

    private static TestVelopackLocator CreateLocator(string version, string packagesDir, string channel)
    {
        var appDir = Path.Combine(packagesDir, "current");
        var rootDir = Path.Combine(packagesDir, "root");
        var updateExe = Path.Combine(packagesDir, "Update.exe");

        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(rootDir);
        File.WriteAllText(updateExe, string.Empty);

        return new TestVelopackLocator(
            appId: PackageId,
            version: version,
            packagesDir: packagesDir,
            appDir: appDir,
            rootDir: rootDir,
            updateExe: updateExe,
            channel: channel);
    }

    private static void WriteFeed(string directory, string channel, params string[] versions)
    {
        var feedPath = Path.Combine(directory, $"releases.{channel}.json");

        var feed = new VelopackAssetFeedFile
        {
            Assets = [.. versions
                .Select(version => new VelopackAssetFile
                {
                    PackageId = PackageId,
                    Version = version,
                    Type = "Full",
                    FileName = $"{PackageId}-{version}-full.nupkg",
                    SHA1 = Sha1,
                    Size = 1000,
                })],
        };

        File.WriteAllText(feedPath, JsonSerializer.Serialize(feed));
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        public TempDirectoryScope()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "aps-generator-update-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup failures in tests.
            }
        }
    }

    private sealed class FakeGithubDownloader : IFileDownloader
    {
        private readonly string _releaseListJson;
        private readonly Dictionary<string, string> _assetFeedByUrl;

        private FakeGithubDownloader(string releaseListJson, Dictionary<string, string> assetFeedByUrl)
        {
            _releaseListJson = releaseListJson;
            _assetFeedByUrl = assetFeedByUrl;
        }

        public static FakeGithubDownloader Create(string channel)
        {
            var stableBrowserUrl = $"https://example.test/stable/releases.{channel}.json";
            var stableApiUrl = $"https://api.example.test/stable/releases.{channel}.json";
            var prereleaseBrowserUrl = $"https://example.test/prerelease/releases.{channel}.json";
            var prereleaseApiUrl = $"https://api.example.test/prerelease/releases.{channel}.json";

            var releases = new object[]
            {
                new
                {
                    name = "v1.1.0",
                    prerelease = false,
                    draft = false,
                    published_at = "2025-01-01T00:00:00Z",
                    assets = new object[]
                    {
                        new
                        {
                            name = $"releases.{channel}.json",
                            browser_download_url = stableBrowserUrl,
                            url = stableApiUrl,
                            content_type = "application/json",
                        },
                    },
                },
                new
                {
                    name = "v2.0.0-beta",
                    prerelease = true,
                    draft = false,
                    published_at = "2025-02-01T00:00:00Z",
                    assets = new object[]
                    {
                        new
                        {
                            name = $"releases.{channel}.json",
                            browser_download_url = prereleaseBrowserUrl,
                            url = prereleaseApiUrl,
                            content_type = "application/json",
                        },
                    },
                },
            };

            var assetFeedByUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [stableBrowserUrl] = BuildSingleVersionFeed("1.1.0"),
                [stableApiUrl] = BuildSingleVersionFeed("1.1.0"),
                [prereleaseBrowserUrl] = BuildSingleVersionFeed("2.0.0"),
                [prereleaseApiUrl] = BuildSingleVersionFeed("2.0.0"),
            };

            return new FakeGithubDownloader(
                releaseListJson: JsonSerializer.Serialize(releases),
                assetFeedByUrl: assetFeedByUrl);
        }

        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            if (_assetFeedByUrl.TryGetValue(url, out var feedJson))
            {
                return Task.FromResult(feedJson);
            }

            if (url.Contains("/releases", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(_releaseListJson);
            }

            throw new InvalidOperationException($"Unexpected URL requested in test downloader: {url}");
        }

        public async Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            var text = await DownloadString(url, headers, timeout);
            return Encoding.UTF8.GetBytes(text);
        }

        public Task DownloadFile(
            string url,
            string targetFile,
            Action<int> progress,
            IDictionary<string, string>? headers = null,
            double timeout = 30,
            CancellationToken cancelToken = default)
        {
            throw new NotSupportedException("DownloadFile should not be called in check-for-updates tests.");
        }

        private static string BuildSingleVersionFeed(string version)
        {
            var feed = new VelopackAssetFeedFile
            {
                Assets =
                [
                    new VelopackAssetFile
                    {
                        PackageId = PackageId,
                        Version = version,
                        Type = "Full",
                        FileName = $"{PackageId}-{version}-full.nupkg",
                        SHA1 = Sha1,
                        Size = 1000,
                    },
                ],
            };

            return JsonSerializer.Serialize(feed);
        }
    }

    private sealed class VelopackAssetFeedFile
    {
        public VelopackAssetFile[] Assets { get; set; } = [];
    }

    private sealed class VelopackAssetFile
    {
        public string PackageId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string SHA1 { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}
