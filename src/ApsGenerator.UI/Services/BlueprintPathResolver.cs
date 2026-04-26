namespace ApsGenerator.UI.Services;

public static class BlueprintPathResolver
{
    private const string GameRelativePath = "From The Depths/Player Profiles";
    private const string PrefabsFolderName = "PrefabsVersion2";
    private const string SkippedProfileName = "temp";
    public static string? Resolve()
    {
        foreach (var basePath in GetCandidateBasePaths())
        {
            if (!Directory.Exists(basePath))
                continue;

            var result = FindPrefabsFolder(basePath);
            if (result is not null)
                return result;
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateBasePaths()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (OperatingSystem.IsLinux())
        {
            if (!string.IsNullOrEmpty(documents))
                yield return Path.Combine(documents, GameRelativePath);

            if (!string.IsNullOrEmpty(userProfile))
            {
                var userDocuments = Path.Combine(userProfile, "Documents");
                if (!string.Equals(documents, userDocuments, StringComparison.Ordinal))
                    yield return Path.Combine(userDocuments, GameRelativePath);

                yield return Path.Combine(userProfile, ".local/share", GameRelativePath);
                yield return Path.Combine(
                    userProfile,
                    GameRelativePath);
                yield return Path.Combine(
                    userProfile,
                    GameRelativePath);
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            if (!string.IsNullOrEmpty(documents))
                yield return Path.Combine(documents, GameRelativePath);

            if (string.IsNullOrEmpty(documents) && !string.IsNullOrEmpty(userProfile))
                yield return Path.Combine(userProfile, "Documents", GameRelativePath);
        }
    }

    private static string? FindPrefabsFolder(string profilesPath)
    {
        try
        {
            foreach (var profileDir in Directory.EnumerateDirectories(profilesPath))
            {
                var profileName = Path.GetFileName(profileDir);
                if (string.Equals(profileName, SkippedProfileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var prefabsPath = Path.Combine(profileDir, PrefabsFolderName);
                if (Directory.Exists(prefabsPath))
                    return prefabsPath;
            }
        }
        catch (IOException)
        {
            // Directory access failed, skip
        }
        catch (UnauthorizedAccessException)
        {
            // Permission denied, skip
        }

        return null;
    }
}
