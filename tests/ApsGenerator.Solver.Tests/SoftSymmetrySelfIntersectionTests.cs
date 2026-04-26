using ApsGenerator.Core;
using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Tests;

/// <summary>
/// Tests for Bug 2: Soft symmetry must allow asymmetric placements
/// when orbit members share exclusive cells (self-intersecting orbits).
///
/// Grid: 7×5, columns 0 and 6 blocked. Vertical reflection axis at col 3.
/// Valid 4-clip centers: (r, 2/3/4) for r ∈ {1,2,3}.
/// Orbits (r,2)↔(r,4) share cell (r,3) — self-intersecting.
/// Orbits (r,3)↔(r,3) — self-map, size 1.
///
/// Soft mode: self-intersecting orbits should impose no constraint,
/// allowing asymmetric placements like (1,2)+(3,4) = 2 clusters.
/// Hard mode: self-intersecting orbits forbidden, only center-column
/// self-maps available, which mutually conflict → 1 cluster max.
/// </summary>
public sealed class SoftSymmetrySelfIntersectionTests
{
    [Fact]
    public void SoftVerticalReflection_SelfIntersectingOrbit_AllowsAsymmetricPlacement()
    {
        var grid = CreateBlockedColumnsGrid(width: 7, height: 5, blockedCols: [0, 6]);
        var solver = new TetrisSolver();
        var options = new SolverOptions
        {
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Soft
        };

        SolverResult result = solver.Solve(grid, TetrisType.FourClip, options);

        AssertPlacementsValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 2,
            $"Soft mode should allow asymmetric near-axis placements. Got {result.ClusterCount} cluster(s).");
    }

    [Fact]
    public void HardVerticalReflection_SelfIntersectingOrbit_ForbidsNearAxisPlacements()
    {
        var grid = CreateBlockedColumnsGrid(width: 7, height: 5, blockedCols: [0, 6]);
        var solver = new TetrisSolver();
        var options = new SolverOptions
        {
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Hard
        };

        SolverResult result = solver.Solve(grid, TetrisType.FourClip, options);

        AssertPlacementsValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount <= 1,
            $"Hard mode should forbid self-intersecting orbit members. Got {result.ClusterCount} cluster(s).");
    }

    [Fact]
    public void SoftHorizontalReflection_SelfIntersectingOrbit_AllowsAsymmetricPlacement()
    {
        // Same scenario rotated: 5×7 grid, rows 0 and 6 blocked.
        // Horizontal reflection axis at row 3.
        var grid = CreateBlockedRowsGrid(width: 5, height: 7, blockedRows: [0, 6]);
        var solver = new TetrisSolver();
        var options = new SolverOptions
        {
            SymmetryType = SymmetryType.HorizontalReflection,
            SymmetryMode = SymmetryMode.Soft
        };

        SolverResult result = solver.Solve(grid, TetrisType.FourClip, options);

        AssertPlacementsValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 2,
            $"Soft mode should allow asymmetric near-axis placements. Got {result.ClusterCount} cluster(s).");
    }

    private static Grid CreateBlockedColumnsGrid(int width, int height, int[] blockedCols)
    {
        var grid = TemplateGenerator.Rectangle(width, height);
        foreach (int col in blockedCols)
            for (int row = 0; row < height; row++)
                grid[row, col] = CellState.Blocked;
        return grid;
    }

    private static Grid CreateBlockedRowsGrid(int width, int height, int[] blockedRows)
    {
        var grid = TemplateGenerator.Rectangle(width, height);
        foreach (int row in blockedRows)
            for (int col = 0; col < width; col++)
                grid[row, col] = CellState.Blocked;
        return grid;
    }

    private static void AssertPlacementsValid(Grid grid, TetrisType type, SolverResult result)
    {
        var shapes = ClusterShape.GetShapes(type);
        var occupiedExclusive = new HashSet<(int, int)>();

        foreach (Placement placement in result.Placements)
        {
            Assert.InRange(placement.ShapeIndex, 0, shapes.Count - 1);
            var offsets = shapes[placement.ShapeIndex].Offsets;

            foreach (CellOffset offset in offsets)
            {
                int r = placement.Row + offset.DeltaRow;
                int c = placement.Col + offset.DeltaCol;
                Assert.True(grid.IsInBounds(r, c), $"Cell ({r},{c}) out of bounds.");
                Assert.True(grid.IsAvailable(r, c), $"Cell ({r},{c}) is blocked.");

                if (offset.Role != CellRole.Connection)
                    Assert.True(occupiedExclusive.Add((r, c)), $"Exclusive cell ({r},{c}) occupied twice.");
            }
        }
    }
}
