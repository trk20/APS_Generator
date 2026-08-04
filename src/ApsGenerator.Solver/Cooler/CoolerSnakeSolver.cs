using System.Diagnostics;
using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

/// <summary>
/// Cooler-snake generation: post-process after Tetris.
/// Order: constructive, undirected intake SAT + Steiner, bridge.
/// Blocked-separated Available regions are solved as independent sectors.
/// </summary>
public sealed class CoolerSnakeSolver
{
    public CoolerSnakeResult Solve(
        Grid grid,
        TetrisType type,
        IReadOnlyList<Placement> placements,
        CoolerSnakeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new CoolerSnakeOptions();

        if (type == TetrisType.FiveClip)
            return FiveClipSnake.Solve(grid, placements, cancellationToken);

        var packing = PackingAnalyzer.Analyze(grid, type, placements);
        return SolveWithSectors(grid, packing, options, cancellationToken);
    }

    /// <summary>
    /// Solve from a pre-built packing analysis (for tests).
    /// With <paramref name="grid"/>, partitions into Available sectors; without, solves the packing as one domain.
    /// </summary>
    internal CoolerSnakeResult Solve(
        PackingAnalysis packing,
        CoolerSnakeOptions? options = null,
        Grid? grid = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new CoolerSnakeOptions();

        if (packing.TetrisType == TetrisType.FiveClip)
        {
            if (grid is null)
                throw new ArgumentException("FiveClip cooler solve requires a grid.", nameof(grid));

            var placements = packing.Clusters
                .Where(c => c.Placement is not null)
                .Select(c => c.Placement!.Value)
                .ToList();
            return FiveClipSnake.Solve(grid, placements, cancellationToken);
        }

        if (grid is not null)
            return SolveWithSectors(grid, packing, options, cancellationToken);

        return SolveCore(packing, options, gridForEjectors: null, cancellationToken);
    }

    private static CoolerSnakeResult SolveWithSectors(
        Grid grid,
        PackingAnalysis packing,
        CoolerSnakeOptions options,
        CancellationToken cancellationToken)
    {
        if (packing.Clusters.Count == 0)
        {
            return new CoolerSnakeResult
            {
                Status = CoolerSnakeStatus.Sat,
                Detail = "empty packing",
                RequiredIntakesPerCluster = EjectorCatalog.RequiredTotalIntakes(packing.TetrisType),
            };
        }

        var slices = CoolerSectors.PartitionOccupied(grid, packing);
        if (slices.Count == 0)
        {
            return new CoolerSnakeResult
            {
                Status = CoolerSnakeStatus.Unsat,
                Detail = "no available sectors",
                RequiredIntakesPerCluster = EjectorCatalog.RequiredTotalIntakes(packing.TetrisType),
            };
        }

        var sw = Stopwatch.StartNew();
        var parts = new List<(CoolerSnakeResult Result, int[] LocalToGlobal, int SnakeId)>(slices.Count);

        foreach (var slice in slices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sectorResult = SolveCore(
                slice.Packing,
                WithBudget(options, RemainingBudget(sw, options)),
                grid,
                cancellationToken);

            if (sectorResult.Status != CoolerSnakeStatus.Sat)
            {
                sw.Stop();
                return sectorResult with
                {
                    SolveSeconds = sw.Elapsed.TotalSeconds,
                    Detail = slices.Count == 1
                        ? sectorResult.Detail
                        : $"sector {slice.SnakeId}: {sectorResult.Detail}",
                };
            }

            parts.Add((sectorResult, slice.LocalToGlobal, slice.SnakeId));
        }

        sw.Stop();
        var merged = CoolerSectors.Merge(parts, packing.Clusters.Count, packing.TetrisType);
        return merged with { SolveSeconds = sw.Elapsed.TotalSeconds };
    }

    private static CoolerSnakeOptions WithBudget(CoolerSnakeOptions options, double budget) => new()
    {
        MaxTimeSeconds = budget,
        Threads = options.Threads,
        OmitEjectors = options.OmitEjectors,
        UndirectedRandomTrials = options.UndirectedRandomTrials,
    };

    private static double RemainingBudget(Stopwatch sw, CoolerSnakeOptions options) =>
        Math.Max(CoolerSolverConstants.MinRemainingBudgetSeconds, options.MaxTimeSeconds - sw.Elapsed.TotalSeconds);

    private static CoolerSnakeResult SolveCore(
        PackingAnalysis packing,
        CoolerSnakeOptions options,
        Grid? gridForEjectors,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();

        var catalog = options.OmitEjectors
            ? EjectorCatalog.BuildIntakeOnly(packing)
            : EjectorCatalog.Build(packing, gridForEjectors);

        var ctx = CoolerRoutingContext.From(packing);

        var constructiveHit = TryConstructiveStage(ctx, packing, catalog, sw, cancellationToken);
        if (constructiveHit is not null)
            return constructiveHit;

        cancellationToken.ThrowIfCancellationRequested();
        var undirected = CoolerSnakeUndirected.TrySolve(
            ctx, catalog, WithBudget(options, RemainingBudget(sw, options)), cancellationToken);
        if (undirected.Satisfiable)
        {
            sw.Stop();
            return CoolerStageResult.ToCoolerSnakeResult(
                undirected, packing, catalog, sw.Elapsed.TotalSeconds, undirected.Detail);
        }

        return FinishWithBridgeOrFail(ctx, packing, catalog, options, sw, undirected, cancellationToken);
    }

    private static CoolerSnakeResult? TryConstructiveStage(
        CoolerRoutingContext ctx,
        PackingAnalysis packing,
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var constructive = CoolerSnakeConstructive.TrySolve(ctx, catalog, cancellationToken);
        if (constructive is not { Satisfiable: true })
            return null;

        sw.Stop();
        return CoolerStageResult.ToCoolerSnakeResult(
            constructive, packing, catalog, sw.Elapsed.TotalSeconds, constructive.Detail);
    }

    private static CoolerSnakeResult FinishWithBridgeOrFail(
        CoolerRoutingContext ctx,
        PackingAnalysis packing,
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        CoolerSnakeOptions options,
        Stopwatch sw,
        CoolerStageResult undirected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        double remBridge = RemainingBudget(sw, options);
        if (remBridge >= CoolerSolverConstants.MinBridgeAttemptBudgetSeconds)
        {
            var bridged = LocalIntakeBridge.TrySolve(
                ctx, catalog, WithBudget(options, remBridge), cancellationToken);
            if (bridged.Satisfiable)
            {
                sw.Stop();
                return CoolerStageResult.ToCoolerSnakeResult(
                    bridged, packing, catalog, sw.Elapsed.TotalSeconds, bridged.Detail);
            }

            sw.Stop();
            return FailResult(sw, packing, undirected, bridged.Detail);
        }

        sw.Stop();
        return FailResult(sw, packing, undirected, undirected.Detail);
    }

    private static CoolerSnakeResult FailResult(
        Stopwatch sw,
        PackingAnalysis packing,
        CoolerStageResult undirected,
        string detail) =>
        new()
        {
            Status = undirected.TimedOut ? CoolerSnakeStatus.TimedOut : CoolerSnakeStatus.Unsat,
            EjectorDirs = [],
            SolveSeconds = sw.Elapsed.TotalSeconds,
            Detail = detail,
            RequiredIntakesPerCluster = EjectorCatalog.RequiredTotalIntakes(packing.TetrisType),
        };
}
