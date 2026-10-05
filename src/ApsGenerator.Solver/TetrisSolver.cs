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
