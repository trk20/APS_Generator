using System.Diagnostics;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

public sealed class TetrisSolver
{
    public SolverResult Solve(Grid grid, TetrisType type, SolverOptions? options = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var settings = options ?? new SolverOptions();
        ValidateOptions(settings);
        var problem = new PackingProblem(grid, type, settings);
        if (problem.Placements.Count == 0)
            return EmptyResult(grid.AvailableCellCount, SolverStatus.NoSolution);

        if (stopwatch.Elapsed.TotalSeconds >= settings.MaxTimeSeconds)
            return EmptyResult(grid.AvailableCellCount, SolverStatus.TimedOut);
        using var initial = new PackingSolverSession(problem, settings, stopwatch, ct);
        var feasibility = initial.Solve();
        if (feasibility != CryptoMiniSatNative.Lbool.True)
            return EmptyResult(grid.AvailableCellCount, feasibility == CryptoMiniSatNative.Lbool.Undef
                ? SolverStatus.TimedOut : SolverStatus.NoSolution);

        var model = initial.Solver.GetModel();
        initial.Dispose();
        var optimized = PackingOptimization.Solve(problem, model, settings, stopwatch, ct);
        var result = problem.Decode(optimized.Model, optimized.Status);
        return settings.NumSolutions > 1
            ? PackingEnumeration.Solve(problem, optimized.Model, result, settings, stopwatch, ct)
            : result;
    }

    /// <summary>
    /// Finds a layout with at least <paramref name="minimumClusters"/> clusters that differs from
    /// every layout in <paramref name="excluded"/>. Returns an empty result with
    /// <see cref="SolverStatus.NoSolution"/> when no such layout exists, or
    /// <see cref="SolverStatus.TimedOut"/> when none was found within the time limit.
    /// </summary>
    public SolverResult SolveAlternative(Grid grid, TetrisType type, SolverOptions options,
        IReadOnlyList<IReadOnlyList<Placement>> excluded, int minimumClusters, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (excluded is null) throw new ArgumentNullException(nameof(excluded));
        ValidateOptions(options);
        if (minimumClusters < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumClusters));
        var stopwatch = Stopwatch.StartNew();
        var problem = new PackingProblem(grid, type, options);
        if (problem.Placements.Count == 0)
            return EmptyResult(grid.AvailableCellCount, SolverStatus.NoSolution);
        return PackingAlternative.Solve(problem, excluded, minimumClusters, options, stopwatch, ct);
    }

    private static void ValidateOptions(SolverOptions options)
    {
        if (options.MaxThreads < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MaxThreads));
        if (double.IsNaN(options.MaxTimeSeconds) || double.IsInfinity(options.MaxTimeSeconds) || options.MaxTimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxTimeSeconds));
        if (options.NumSolutions < 1)
            throw new ArgumentOutOfRangeException(nameof(options.NumSolutions));
        if (options.TargetClusterCount < 0)
            throw new ArgumentOutOfRangeException(nameof(options.TargetClusterCount));
    }

    private static SolverResult EmptyResult(int cells, SolverStatus status) => new()
    {
        Placements = [], AllSolutions = [], EmptyCells = cells, Status = status
    };
}
