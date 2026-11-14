using ApsGenerator.Core.Models;

namespace ApsGenerator.UI.Services;

internal static class PlacementTargeting
{
    public static int TheoreticalMaxClusters(TetrisType type, int availableCells) =>
        type.TheoreticalMaxClusters(availableCells);

    public static int FromRatio(double ratio, int maxPlacements)
    {
        if (maxPlacements <= 0)
            return 0;
        int min = maxPlacements / 3;
        int value = (int)Math.Round(ratio * maxPlacements);
        return Math.Clamp(value, min, maxPlacements);
    }
}
