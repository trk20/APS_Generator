using ApsGenerator.Mod.Generation;
using BrilliantSkies.PlayerProfiles;

namespace ApsGenerator.Mod;

internal sealed class ApsSettingsProfile : ProfileModule<ApsSettingsData>
{
    private const string ProfileFileName = "profile.apsgenerator";

    public ApsSettingsProfile()
    {
    }

    protected override string FilenameAndExtension => ProfileFileName;

    public override ModuleType ModuleType => ModuleType.Options;

    internal ApsSettingsData Data => GetInternalData();

    internal void ReplaceData(ApsSettingsData data) => Internal = data;
}
