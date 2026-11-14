using ApsGenerator.Core.Models;
using ApsGenerator.UI.Services;

namespace ApsGenerator.UI.Tests;

[Trait("Category", "Unit")]
public sealed class UserSettingsMigrationTests
{
    [Fact]
    public void UnversionedSettingsMoveUnchangedSolverDefaultsToTheCurrentDefaults()
    {
        var legacy = new UserSettings
        {
            SettingsVersion = 1,
            SelectedSymmetryType = SymmetryType.None,
            MaxTimeSeconds = 30,
            EarlyStopEnabled = true,
            ThreadCount = ApsGenerationDefaults.MaximumThreadCount
        };

        UserSettings migrated = UserSettingsStore.MigrateLegacyDefaults(legacy);

        Assert.Equal(ApsGenerationDefaults.DefaultSymmetryType, migrated.SelectedSymmetryType);
        Assert.Equal(ApsGenerationDefaults.MaxTimeSeconds, migrated.MaxTimeSeconds);
        Assert.False(migrated.EarlyStopEnabled);
        Assert.Equal(ApsGenerationDefaults.DefaultThreadCount, migrated.ThreadCount);
        Assert.Equal(UserSettings.CurrentSettingsVersion, migrated.SettingsVersion);
    }

    [Fact]
    public void ChangedValuesAndCurrentVersionSettingsAreKept()
    {
        var legacy = new UserSettings
        {
            SettingsVersion = 1,
            SelectedSymmetryType = SymmetryType.Rotation180,
            MaxTimeSeconds = 45,
        };
        UserSettings migrated = UserSettingsStore.MigrateLegacyDefaults(legacy);
        Assert.Equal(SymmetryType.Rotation180, migrated.SelectedSymmetryType);
        Assert.Equal(45, migrated.MaxTimeSeconds);

        var current = new UserSettings { SelectedSymmetryType = SymmetryType.None, MaxTimeSeconds = 30 };
        Assert.Equal(SymmetryType.None, UserSettingsStore.MigrateLegacyDefaults(current).SelectedSymmetryType);
        Assert.Equal(30, UserSettingsStore.MigrateLegacyDefaults(current).MaxTimeSeconds);
    }
}
