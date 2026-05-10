using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace ApsGenerator.UI.Services;

public static class UpdateService
{
    private const string RepoUrl = "https://github.com/trk20/APS_Generator";

    public static UpdateManager CreateUpdateManager(bool receiveExperimental)
    {
        var source = new GithubSource(RepoUrl, null, receiveExperimental);
        return new UpdateManager(source, new UpdateOptions { AllowVersionDowngrade = true });
    }

    public static UpdateManager CreateUpdateManager(IUpdateSource source, IVelopackLocator locator)
    {
        return new UpdateManager(source, new UpdateOptions { AllowVersionDowngrade = true }, locator);
    }

    public static string? GetCurrentVersion()
    {
        var asm = typeof(UpdateService).Assembly;
        var version = asm.GetName().Version;
        return version is null ? null : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}