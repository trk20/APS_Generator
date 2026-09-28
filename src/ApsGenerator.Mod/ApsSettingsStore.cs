using ApsGenerator.Mod.Generation;
using BrilliantSkies.Core.Logger;
using BrilliantSkies.PlayerProfiles;

namespace ApsGenerator.Mod;

internal sealed class ApsSettingsStore
{
    private readonly IProfileManager profileManager;
    private ApsSettingsProfile? profile;
    private bool canSave = true;

    internal ApsSettingsStore()
        : this(ProfileManager.Instance)
    {
    }

    internal ApsSettingsStore(IProfileManager profileManager)
    {
        this.profileManager = profileManager;
    }

    internal void LoadInto(ApsGenerationSettings settings)
    {
        try
        {
            profile = profileManager.GetModule<ApsSettingsProfile>();
            ApsSettingsData data = profile.Data;
            canSave = ApsSettingsData.IsSupportedSchemaVersion(data.SchemaVersion);
            settings.Apply(data);
        }
        catch (Exception error)
        {
            AdvLogger.LogException("[APS Generator] Failed to load saved settings", error, LogOptions.None);
        }
    }

    internal void Save(ApsGenerationSettings settings)
    {
        if (!canSave)
            return;

        try
        {
            profile ??= profileManager.GetModule<ApsSettingsProfile>();
            profile.ReplaceData(settings.Capture());
            profileManager.Save(module => ReferenceEquals(module, profile));
        }
        catch (Exception error)
        {
            AdvLogger.LogException("[APS Generator] Failed to save settings", error, LogOptions.None);
        }
    }
}
