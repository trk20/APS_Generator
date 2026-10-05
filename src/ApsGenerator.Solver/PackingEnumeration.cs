using System.Diagnostics;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

internal static class PackingEnumeration
{
    public static SolverResult Solve(PackingProblem problem, bool?[] firstModel,
        SolverResult firstResult, SolverOptions options, Stopwatch stopwatch, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (stopwatch.Elapsed.TotalSeconds >= options.MaxTimeSeconds)
            return firstResult;
        using var session = new PackingSolverSession(problem, options, stopwatch, cancellation);
        var objective = problem.EncodeObjective(session.Solver, ref session.NextVariable);
        SatTotalizer.AddWeightedAtMostK(session.Solver, objective.Terms,
            objective.ReduceBound(firstResult.EmptyCells), ref session.NextVariable);
        if (firstResult.Status != SolverStatus.Optimal)
        {
            // An unproved empty-cell bound permits denser layouts. Also cap placement
            // count so every returned layout shares the result's density metadata.
            var placements = Enumerable.Range(1, problem.Placements.Count).ToArray();
            SatTotalizer.AddAtMostK(session.Solver, placements, firstResult.ClusterCount, ref session.NextVariable);
        }
        if (session.Solve() != CryptoMiniSatNative.Lbool.True)
            return firstResult;

        var solutions = new List<IReadOnlyList<Placement>> { firstResult.Placements };
        var model = firstModel;
        while (solutions.Count < options.NumSolutions)
        {
            var blocking = new int[problem.Placements.Count];
            for (int index = 0; index < blocking.Length; index++)
                blocking[index] = model[index] == true ? -(index + 1) : index + 1;
            session.Solver.AddClause(blocking);
            if (session.Solve() != CryptoMiniSatNative.Lbool.True)
                break;
            model = session.Solver.GetModel();
            var decoded = problem.Decode(model, firstResult.Status);
            solutions.Add(decoded.Placements);
        }
        return firstResult with { AllSolutions = solutions };
    }
}
