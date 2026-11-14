using System.Diagnostics;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

/// <summary>
/// Finds one more layout, at least as dense as a cluster floor, that differs from every
/// layout already shown. Each call builds a fresh instance so callers need not keep a
/// native solver alive between requests.
/// </summary>
internal static class PackingAlternative
{
    public static SolverResult Solve(PackingProblem problem, IReadOnlyList<IReadOnlyList<Placement>> excluded,
        int minimumClusters, SolverOptions options, Stopwatch stopwatch, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var session = new PackingSolverSession(problem, options, stopwatch, cancellation);
        var objective = problem.EncodeObjective(session.Solver, ref session.NextVariable);
        int emptyBound = objective.ReduceBound(problem.AvailableCells - minimumClusters * problem.Step);
        if (emptyBound < 0)
            return Empty(problem, SolverStatus.NoSolution);
        SatTotalizer.AddWeightedAtMostK(session.Solver, objective.Terms, emptyBound, ref session.NextVariable);

        var indices = new Dictionary<Placement, int>(problem.Placements.Count);
        for (int index = 0; index < problem.Placements.Count; index++)
            indices[problem.Placements[index]] = index;
        foreach (var layout in excluded)
            session.Solver.AddClause(BlockingClause(layout, indices, problem.Placements.Count));

        var result = session.Solve();
        if (result != CryptoMiniSatNative.Lbool.True)
            return Empty(problem, result == CryptoMiniSatNative.Lbool.Undef
                ? SolverStatus.TimedOut : SolverStatus.NoSolution);
        return problem.Decode(session.Solver.GetModel(), SolverStatus.LikelyOptimal);
    }

    private static int[] BlockingClause(IReadOnlyList<Placement> layout, Dictionary<Placement, int> indices, int count)
    {
        var selected = new bool[count];
        foreach (var placement in layout)
            if (indices.TryGetValue(placement, out int index))
                selected[index] = true;
        var clause = new int[count];
        for (int index = 0; index < count; index++)
            clause[index] = selected[index] ? -(index + 1) : index + 1;
        return clause;
    }

    private static SolverResult Empty(PackingProblem problem, SolverStatus status) => new()
    {
        Placements = [], AllSolutions = [], EmptyCells = problem.AvailableCells, Status = status
    };
}
