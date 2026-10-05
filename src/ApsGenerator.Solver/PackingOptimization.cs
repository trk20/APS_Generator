using System.Diagnostics;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

internal static class PackingOptimization
{
    internal readonly record struct Result(bool?[] Model, SolverStatus Status);

    public static Result Solve(PackingProblem problem, bool?[] model, SolverOptions options,
        Stopwatch stopwatch, CancellationToken cancellation)
    {
        int emptyCells = problem.CountEmptyCells(model);
        if (emptyCells <= 0)
            return new(model, SolverStatus.Optimal);
        if (HasReachedTarget(problem, model, options))
            return new(model, SolverStatus.TargetDensityReached);
        int bound = problem.GetNextBound(emptyCells);
        if (bound < 0)
            return new(model, SolverStatus.Optimal);

        cancellation.ThrowIfCancellationRequested();
        if (stopwatch.Elapsed.TotalSeconds >= options.MaxTimeSeconds)
            return new(model, SolverStatus.TimedOut);
        using var session = new PackingSolverSession(problem, options, stopwatch, cancellation);
        var objective = problem.EncodeObjective(session.Solver, ref session.NextVariable);
        int maximum = Math.Max(0, objective.ReduceBound(bound));
        var root = SatTotalizer.BuildAtMostKCounter(session.Solver, objective.Terms,
            maximum, ref session.NextVariable);
        var earlyStop = new PackingEarlyStop();
        while (bound >= 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (session.RemainingSeconds <= 0)
                return new(model, SolverStatus.TimedOut);
            int reducedBound = objective.ReduceBound(bound);
            if (reducedBound < 0)
                return new(model, SolverStatus.Optimal);

            int? assumption = SatTotalizer.GetAtMostKAssumption(root, reducedBound);
            var iteration = Stopwatch.StartNew();
            var result = session.Solve(assumption, useAssumptions: true);
            iteration.Stop();
            if (result != CryptoMiniSatNative.Lbool.True)
                return new(model, result == CryptoMiniSatNative.Lbool.Undef
                    ? SolverStatus.TimedOut : SolverStatus.Optimal);
            model = session.Solver.GetModel();
            if (HasReachedTarget(problem, model, options))
                return new(model, SolverStatus.TargetDensityReached);
            if (earlyStop.Record(iteration.Elapsed.TotalMilliseconds) && options.EarlyStopEnabled)
                return new(model, SolverStatus.LikelyOptimal);
            bound = problem.GetNextBound(problem.CountEmptyCells(model));
        }
        return new(model, SolverStatus.Optimal);
    }

    private static bool HasReachedTarget(PackingProblem problem, bool?[] model, SolverOptions options) =>
        options.TargetClusterCount.HasValue && problem.CountClusters(model) >= options.TargetClusterCount.Value;
}
