namespace ApsGenerator.Solver.Cooler;

internal static class CoolerSolverConstants
{
    public const double MinRemainingBudgetSeconds = 0.05;
    public const double MinBridgeAttemptBudgetSeconds = 0.1;
    public const int GoodEnoughBridgeCellCap = 8;
    public const int ConstructiveRandomSeed = 7;
    public const int ConstructiveRandomTrials = 24;
    public const int ConstructiveIntakeSeed = 11;
    public const int ConstructiveIntakeTrials = 40;
    public const int UndirectedRandomSeed = 3;
    public const int UndirectedRandomTrials = 6;
    public const int BridgeRandomSeed = 19;
    public const int BridgeIntakeTrials = 48;
    public const int BridgeRefineMaxPasses = 4;
    public const int BridgeRefineMaxMovesPerPass = 16;

    /// <summary>Cell-count threshold above which reachability depth is capped at <see cref="ReachabilityDepthLarge"/>.</summary>
    public const int ReachabilityTierLargeCells = 200;

    /// <summary>Cell-count threshold above which reachability depth is capped at <see cref="ReachabilityDepthMedium"/>.</summary>
    public const int ReachabilityTierMediumCells = 120;

    /// <summary>Cell-count threshold above which reachability depth is capped at <see cref="ReachabilityDepthSmall"/>.</summary>
    public const int ReachabilityTierSmallCells = 60;

    public const int ReachabilityDepthLarge = 48;
    public const int ReachabilityDepthMedium = 56;
    public const int ReachabilityDepthSmall = 72;

    /// <summary>Shared reachability depth policy for directed and undirected cooler SAT encodings.</summary>
    public static int ReachabilityMaxDepth(int cellCount)
    {
        int uncapped = cellCount - 1;
        return uncapped switch
        {
            <= 0 => 0,
            <= ReachabilityTierSmallCells => Math.Min(uncapped, ReachabilityDepthSmall),
            <= ReachabilityTierMediumCells => Math.Min(uncapped, ReachabilityDepthMedium),
            <= ReachabilityTierLargeCells => Math.Min(uncapped, ReachabilityDepthLarge),
            _ => Math.Min(uncapped, ReachabilityDepthLarge),
        };
    }
}
