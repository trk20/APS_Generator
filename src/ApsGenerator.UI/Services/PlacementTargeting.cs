using ApsGenerator.Core.Models;

namespace ApsGenerator.UI.Services;

internal static class PlacementTargeting
{
    public static int TheoreticalMaxClusters(TetrisType type, int availableCells) => type switch
    {
        TetrisType.ThreeClip or TetrisType.FourClip => availableCells / type.EffectiveAutoloadersPerPlacement(),
        TetrisType.FiveClip => 2 * availableCells / 9,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported tetris type.")
    };

    public static int FromRatio(double ratio, int maxPlacements)
    {
        if (maxPlacements <= 0)
            return 0;
        int min = maxPlacements / 3;
        int value = (int)Math.Round(ratio * maxPlacements);
        return Math.Clamp(value, min, maxPlacements);
    }
}
