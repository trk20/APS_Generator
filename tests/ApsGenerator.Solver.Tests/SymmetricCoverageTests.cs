using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver.Tests;

public sealed class SymmetricCoverageTests
{
    [Theory]
    [InlineData(2, 3, SymmetryType.VerticalReflection, false)]
    [InlineData(3, 3, SymmetryType.VerticalReflection, false)]
    [InlineData(3, 3, SymmetryType.HorizontalReflection, false)]
    [InlineData(3, 3, SymmetryType.BothReflection, false)]
    [InlineData(3, 3, SymmetryType.Rotation180, false)]
    [InlineData(3, 3, SymmetryType.Rotation90, false)]
    [InlineData(3, 3, SymmetryType.BothReflection, true)]
    [InlineData(3, 3, SymmetryType.Rotation90, true)]
    public void BoundMatchesActualUncoveredCellsForEverySymmetricAssignment(
        int width, int height, SymmetryType symmetry, bool blockCorner)
    {
        var grid = TemplateGenerator.Rectangle(width, height);
        if (blockCorner)
            grid[0, 0] = CellState.Blocked;
        var cells = (from row in Enumerable.Range(0, height)
                     from col in Enumerable.Range(0, width)
                     where grid.IsAvailable(row, col)
                     select (Row: row, Col: col)).ToArray();
        var coverage = cells.Select((cell, index) => (cell, index))
            .ToDictionary(pair => pair.cell, pair => new List<int> { pair.index });

        for (int bound = -1; bound <= cells.Length + 1; bound++)
        {
            using var solver = new SatSolver();
            solver.AddVariables(cells.Length);
            ConstrainSymmetry(solver, grid, cells, coverage, symmetry);
            int nextVariable = cells.Length;
            SymmetricCoverage.AddBound(solver, grid, cells, coverage, symmetry, bound, ref nextVariable);
            for (int bits = 0; bits < 1 << cells.Length; bits++)
            {
                var assumptions = Enumerable.Range(1, cells.Length)
                    .Select(literal => (bits & (1 << (literal - 1))) != 0 ? literal : -literal).ToArray();
                var selected = cells.Where((cell, index) => assumptions[index] > 0).ToHashSet();
                bool symmetric = selected.All(cell => SymmetryTransforms.GetSymmetricPositions(
                    cell.Row, cell.Col, width, height, symmetry).All(selected.Contains));
                bool expected = symmetric && cells.Length - selected.Count <= bound;
                Assert.Equal(expected ? CryptoMiniSatNative.Lbool.True : CryptoMiniSatNative.Lbool.False,
                    solver.SolveWithAssumptions(assumptions));
            }
        }
    }

    private static void ConstrainSymmetry(SatSolver solver, Grid grid,
        (int Row, int Col)[] cells, Dictionary<(int, int), List<int>> coverage, SymmetryType symmetry)
    {
        for (int index = 0; index < cells.Length; index++)
        {
            var cell = cells[index];
            foreach (var other in SymmetryTransforms.GetSymmetricPositions(
                cell.Row, cell.Col, grid.Width, grid.Height, symmetry))
            {
                if (coverage.TryGetValue(other, out var counterpart))
                    solver.AddClause([-(index + 1), counterpart[0] + 1]);
                else
                    solver.AddClause([-(index + 1)]);
            }
        }
    }
}
