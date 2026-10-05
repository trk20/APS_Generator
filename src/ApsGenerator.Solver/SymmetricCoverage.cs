using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

/// <summary>
/// Hard symmetry makes exclusive coverage identical throughout each cell orbit.
/// One coverage literal therefore represents the orbit, weighted by its cell count.
/// Soft symmetry must continue to count cells individually.
/// </summary>
internal static class SymmetricCoverage
{
    internal readonly record struct BoundEncoding(
        IReadOnlyList<WeightedLiteral> Terms,
        int Divisor,
        int FixedEmptyCells)
    {
        public int ReduceBound(int bound) => (int)Math.Floor((double)(bound - FixedEmptyCells) / Divisor);
    }

    public static BoundEncoding BuildEncoding(SatSolver solver, Grid grid,
        IReadOnlyList<(int Row, int Col)> cells,
        Dictionary<(int, int), List<int>> coverage, SymmetryType symmetry,
        ref int nextVariable)
    {
        var visited = new HashSet<(int, int)>();
        var terms = new List<WeightedLiteral>();
        int fixedEmptyCells = 0;

        foreach (var cell in cells)
        {
            if (visited.Contains(cell))
                continue;

            var orbit = SymmetryTransforms.GetSymmetricPositions(cell.Row, cell.Col,
                grid.Width, grid.Height, symmetry);
            visited.UnionWith(orbit);
            int available = orbit.Count(position => grid.IsAvailable(position.Row, position.Col));
            if (available != orbit.Count || !coverage.TryGetValue(cell, out var placements))
            {
                fixedEmptyCells += available;
                continue;
            }

            int covered = AddCoverageLiteral(solver, placements, ref nextVariable);
            terms.Add(new(-covered, orbit.Count));
        }

        int divisor = terms.Count == 0 ? 1 : terms.Select(term => term.Weight).Aggregate(Gcd);
        var reducedTerms = terms.Select(term => term with { Weight = term.Weight / divisor }).ToArray();
        return new(reducedTerms, divisor, fixedEmptyCells);
    }

    private static int Gcd(int left, int right)
    {
        while (right != 0)
            (left, right) = (right, left % right);
        return Math.Abs(left);
    }

    public static void AddBound(SatSolver solver, Grid grid,
        IReadOnlyList<(int Row, int Col)> cells,
        Dictionary<(int, int), List<int>> coverage,
        SymmetryType symmetry, int bound, ref int nextVariable)
    {
        var encoding = BuildEncoding(solver, grid, cells, coverage, symmetry, ref nextVariable);
        int reducedBound = (int)Math.Floor(
            (double)(bound - encoding.FixedEmptyCells) / encoding.Divisor);
        SatTotalizer.AddWeightedAtMostK(solver, encoding.Terms, reducedBound, ref nextVariable);
    }

    private static int AddCoverageLiteral(SatSolver solver, List<int> placements, ref int nextVariable)
    {
        solver.AddVariables(1);
        int covered = ++nextVariable;
        var clause = new int[placements.Count + 1];
        clause[0] = -covered;
        for (int index = 0; index < placements.Count; index++)
            clause[index + 1] = placements[index] + 1;
        solver.AddClause(clause);
        return covered;
    }

}
