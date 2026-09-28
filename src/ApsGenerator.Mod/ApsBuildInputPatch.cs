using BrilliantSkies.Ftd.Avatar.Build;
using BrilliantSkies.Ftd.Avatar.HUD;
using HarmonyLib;
using ApsGenerator.Mod.UI;

namespace ApsGenerator.Mod;

[HarmonyPatch(typeof(cBuild), "DoBuildMode")]
internal static class ApsBuildInputPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !ApsGeneratorBehaviour.ShouldSuppressBuildInput;
}

[HarmonyPatch(typeof(cBuild), "PerformPrefabOperation")]
internal static class ApsPrefabPlaceInputPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !ApsGeneratorBehaviour.ShouldSuppressBuildInput;
}

[HarmonyPatch(typeof(cBuild), nameof(cBuild.EraseArea))]
internal static class ApsPrefabEraseInputPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !ApsGeneratorBehaviour.ShouldSuppressBuildInput;
}

[HarmonyPatch(typeof(HudBuildCommands), "PrefabAndSubObjectOptions")]
internal static class ApsNativePrefabHudPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !ApsGeneratorBehaviour.ShouldReplaceNativePrefabHud;

    [HarmonyPostfix]
    private static void Postfix() => ApsGeneratorBehaviour.DrawPrefabGeneratorButton();
}
