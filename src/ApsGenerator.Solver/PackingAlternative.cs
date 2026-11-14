using System.Diagnostics;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

/// <summary>
/// Finds one more layout at least as dense as a cluster floor that does not contain every
/// placement of any layout already shown. Excluding supersets (not just exact matches) means a
/// layout trimmed down to a density target can't reproduce one already shown. Each call builds a fresh instance.
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
            session.Solver.AddClause(BlockingClause(layout, indices));

        var result = session.Solve();
        if (result != CryptoMiniSatNative.Lbool.True)
            return Empty(problem, result == CryptoMiniSatNative.Lbool.Undef
                ? SolverStatus.TimedOut : SolverStatus.NoSolution);
        return problem.Decode(session.Solver.GetModel(), SolverStatus.LikelyOptimal);
    }

    /// <summary>At least one placement of <paramref name="layout"/> must be left out.</summary>
    private static int[] BlockingClause(IReadOnlyList<Placement> layout, Dictionary<Placement, int> indices) =>
        [.. layout.Select(placement => -(indices[placement] + 1))];

    private static SolverResult Empty(PackingProblem problem, SolverStatus status) => new()
    {
        Placements = [],
        AllSolutions = [],
        EmptyCells = problem.AvailableCells,
        Status = status
    };
}
