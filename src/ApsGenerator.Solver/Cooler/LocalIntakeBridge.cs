using System.Diagnostics;
using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

/// <summary>
/// Local over-intake bridges: keep the deck snake, and only elevate short ramp / over-intake
/// segments when intakes disconnect loader-chain terminals.
/// </summary>
internal static class LocalIntakeBridge
{
    public static CoolerStageResult TrySolve(
        CoolerRoutingContext ctx,
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        CoolerSnakeOptions options,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var best = SearchBestBridge(ctx.Packing, ctx, catalog, options, sw, cancellationToken);
        sw.Stop();

        if (best is not null)
            return best.WithTiming(sw.Elapsed.TotalSeconds);

        return new CoolerStageResult
        {
            Satisfiable = false,
            TimedOut = sw.Elapsed.TotalSeconds >= options.MaxTimeSeconds,
            Detail = CoolerStageDetails.LocalBridgeFailed,
            SolveSeconds = sw.Elapsed.TotalSeconds,
            Layers = 2,
        };
    }

    private static CoolerStageResult? SearchBestBridge(
        PackingAnalysis packing,
        CoolerRoutingContext ctx,
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        CoolerSnakeOptions options,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        CoolerStageResult? bestBridge = null;
        IReadOnlyList<EjectorCandidate>? bestAssignment = null;
        bool[]? bestMask = null;
        int bestBridgeCells = int.MaxValue;
        int bestLoaderTops = int.MaxValue;

        foreach (var assignment in EjectorAssignmentSearch.ForBridge(catalog))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sw.Elapsed.TotalSeconds >= options.MaxTimeSeconds)
                break;

            var hit = SearchAssignmentMasks(
                packing, ctx, assignment, options, sw, cancellationToken,
                ref bestBridge, ref bestAssignment, ref bestBridgeCells, ref bestLoaderTops, ref bestMask);
            if (hit is not null)
                return RefineIfBridged(ctx, packing, assignment, hit, MaskFromTops(ctx, hit));

            if (IsGoodEnough(bestBridgeCells, bestLoaderTops))
                break;
        }

        if (bestBridge is null || bestAssignment is null || bestMask is null)
            return bestBridge;

        return RefineIfBridged(ctx, packing, bestAssignment, bestBridge, bestMask);
    }

    private static CoolerStageResult RefineIfBridged(
        CoolerRoutingContext ctx,
        PackingAnalysis packing,
        IReadOnlyList<EjectorCandidate> assignment,
        CoolerStageResult candidate,
        bool[] intakeMask)
    {
        if (candidate.BridgeCoolerCells.Count == 0)
            return candidate;

        return BridgeRefine.Improve(ctx, packing, assignment, candidate, intakeMask);
    }

    private static CoolerStageResult? SearchAssignmentMasks(
        PackingAnalysis packing,
        CoolerRoutingContext ctx,
        IReadOnlyList<EjectorCandidate> assignment,
        CoolerSnakeOptions options,
        Stopwatch sw,
        CancellationToken cancellationToken,
        ref CoolerStageResult? bestBridge,
        ref IReadOnlyList<EjectorCandidate>? bestAssignment,
        ref int bestBridgeCells,
        ref int bestLoaderTops,
        ref bool[]? bestMask)
    {
        foreach (var intakeMask in CoolerIntakeAssign.EnumerateMaskTrials(
                     ctx,
                     assignment,
                     CoolerSolverConstants.BridgeIntakeTrials,
                     CoolerSolverConstants.BridgeRandomSeed,
                     tryMinimizingComponents: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sw.Elapsed.TotalSeconds >= options.MaxTimeSeconds)
                break;

            var candidate = CoolerGraph.TrySteinerDeckOrBridge(
                ctx, packing, assignment, intakeMask,
                solveSeconds: sw.Elapsed.TotalSeconds);
            if (candidate is null)
                continue;

            if (candidate.BridgeCoolerCells.Count == 0)
                return candidate.WithTiming(sw.Elapsed.TotalSeconds);

            int bridgeCount = candidate.BridgeCoolerCells.Count;
            int loaderTops = packing.LoaderCells.Count(
                cellKey => ctx.IndexOf.TryGetValue(cellKey, out int i) && intakeMask[i]);

            if (bridgeCount > bestBridgeCells)
                continue;
            if (bridgeCount == bestBridgeCells && loaderTops >= bestLoaderTops)
                continue;

            bestBridgeCells = bridgeCount;
            bestLoaderTops = loaderTops;
            bestBridge = candidate;
            bestAssignment = assignment;
            bestMask = (bool[])intakeMask.Clone();

            if (IsGoodEnough(bestBridgeCells, bestLoaderTops))
                break;
        }

        return null;
    }

    private static bool[] MaskFromTops(CoolerRoutingContext ctx, CoolerStageResult stage)
    {
        var mask = new bool[ctx.CellCount];
        foreach (var top in stage.TopIntakes)
        {
            if (ctx.IndexOf.TryGetValue(top, out int i))
                mask[i] = true;
        }

        return mask;
    }

    private static bool IsGoodEnough(int bridgeCells, int loaderTops) =>
        bridgeCells <= CoolerSolverConstants.GoodEnoughBridgeCellCap && loaderTops == 0;
}
