using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver;
using Xunit.Abstractions;

namespace ApsGenerator.Solver.Tests;

public sealed class TetrisSolverTests
{
    private readonly ITestOutputHelper _output;

    public TetrisSolverTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Solve_Grid3x3_AllAvailable_FourClip_FindsSingleCenteredCluster()
    {
        var grid = TemplateGenerator.Rectangle(width: 3, height: 3);

        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip);

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.Equal(1, result.ClusterCount);
        Assert.Equal(4, result.EmptyCells);
        Assert.Contains(result.Placements, p => p is { Row: 1, Col: 1, ShapeIndex: 0 });
    }

    [Fact]
    public void Solve_Grid3x3_AllAvailable_ThreeClip_FindsAtLeastOneValidCluster()
    {
        var grid = TemplateGenerator.Rectangle(width: 3, height: 3);

        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip);

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 1);
    }

    [Fact]
    public void Solve_Circle5x5_BlockedCenter_ThreeClip_FindsAtLeastFourClusters()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);

        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip);

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 4);
    }

    [Fact]
    public void Solve_Grid3x3_AllBlocked_FindsNoClusters()
    {
        var grid = CreateAllBlockedGrid(width: 3, height: 3);

        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip);

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.Equal(0, result.ClusterCount);
        Assert.Equal(0, result.EmptyCells);
    }

    [Fact]
    public void Solve_Grid3x3_CenterBlocked_FourClip_FindsNoClusters()
    {
        var grid = TemplateGenerator.Rectangle(width: 3, height: 3);
        grid[1, 1] = CellState.Blocked;

        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip);

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.Equal(0, result.ClusterCount);
    }

    [Fact]
    public void Solve_ResultPlacements_DoNotOverlapOnLoaderOrClipCells()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);

        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip);

        AssertNoLoaderOrClipOverlap(TetrisType.FiveClip, result);
    }

    [Fact]
    public void Solve_ResultPlacements_AreWithinBoundsAndOnAvailableCells()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);

        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip);

        AssertFootprintsInBoundsAndAvailableCells(grid, TetrisType.ThreeClip, result);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle11x11_BlockedCenter_FourClip_FindsNearOptimalSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: true);

        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip);

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 14);
    }

    [Fact]
    public void Solve_Circle5x5_BlockedCenter_ThreeClip_MatchesKnownReferenceCount()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);

        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip);

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.Equal(4, result.ClusterCount);
    }

    [Fact]
    public void Solve_Circle15x15_BlockedCenter_ThreeClip_DensityTarget40_StopsAt40()
    {
        var grid = TemplateGenerator.Circle(diameter: 15, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions { TargetClusterCount = 40 };
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, opts);

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 40);
    }

    [Fact]
    public void Solve_Circle11x11_BlockedCenter_FourClip_DensityTarget10_StopsAt10()
    {
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions { TargetClusterCount = 10 };
        SolverResult result = solver.Solve(grid, TetrisType.FourClip, opts);

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 10);
    }

    private static void AssertSolutionValid(Grid grid, TetrisType type, SolverResult result)
    {
        AssertShapeIndicesAreValid(type, result);
        AssertFootprintsInBoundsAndAvailableCells(grid, type, result);
        AssertNoLoaderOrClipOverlap(type, result);

        int coveredCellCount = CountUniqueCoveredCells(type, result);
        int expectedEmptyCells = grid.AvailableCellCount - coveredCellCount;

        Assert.Equal(expectedEmptyCells, result.EmptyCells);
    }

    private static void AssertShapeIndicesAreValid(TetrisType type, SolverResult result)
    {
        var shapes = ClusterShape.GetShapes(type);

        foreach (Placement placement in result.Placements)
            Assert.InRange(placement.ShapeIndex, 0, shapes.Count - 1);
    }

    private static void AssertFootprintsInBoundsAndAvailableCells(Grid grid, TetrisType type, SolverResult result)
    {
        var shapes = ClusterShape.GetShapes(type);

        foreach (Placement placement in result.Placements)
        {
            IReadOnlyList<CellOffset> offsets = shapes[placement.ShapeIndex].Offsets;

            foreach (CellOffset offset in offsets)
            {
                int row = placement.Row + offset.DeltaRow;
                int col = placement.Col + offset.DeltaCol;

                Assert.True(grid.IsInBounds(row, col));
                Assert.True(grid.IsAvailable(row, col));
            }
        }
    }

    private static void AssertNoLoaderOrClipOverlap(TetrisType type, SolverResult result)
    {
        var shapes = ClusterShape.GetShapes(type);
        var occupiedByLoaderOrClip = new HashSet<(int Row, int Col)>();

        foreach (Placement placement in result.Placements)
        {
            IReadOnlyList<CellOffset> offsets = shapes[placement.ShapeIndex].Offsets;

            foreach (CellOffset offset in offsets)
            {
                if (offset.Role == CellRole.Connection)
                    continue;

                var cell = (Row: placement.Row + offset.DeltaRow, Col: placement.Col + offset.DeltaCol);
                Assert.True(occupiedByLoaderOrClip.Add(cell));
            }
        }
    }

    private static int CountUniqueCoveredCells(TetrisType type, SolverResult result)
    {
        var shapes = ClusterShape.GetShapes(type);
        var coveredCells = new HashSet<(int Row, int Col)>();

        foreach (Placement placement in result.Placements)
        {
            IReadOnlyList<CellOffset> offsets = shapes[placement.ShapeIndex].Offsets;

            foreach (CellOffset offset in offsets)
            {
                if (offset.Role == CellRole.Connection)
                    continue;

                var cell = (Row: placement.Row + offset.DeltaRow, Col: placement.Col + offset.DeltaCol);
                coveredCells.Add(cell);
            }
        }

        return coveredCells.Count;
    }

    private static Grid CreateAllBlockedGrid(int width, int height)
    {
        var grid = TemplateGenerator.Rectangle(width, height);

        for (int row = 0; row < grid.Height; row++)
        for (int col = 0; col < grid.Width; col++)
            grid[row, col] = CellState.Blocked;

        return grid;
    }

    [Fact]
    public void Solve_Circle15x15_BlockedCenter_ThreeClip_FindsOptimal()
    {
        var grid = TemplateGenerator.Circle(diameter: 15, blockCenter: true);

        var solver = new TetrisSolver();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, new SolverOptions { MaxTimeSeconds = 30 });
        stopwatch.Stop();

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 42, $"Expected >= 42 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle17x17_BlockedCenter_ThreeClip_FindsOptimal()
    {
        var grid = TemplateGenerator.Circle(diameter: 17, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, new SolverOptions { MaxTimeSeconds = 60 });

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 53, $"Expected >= 53 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle11x11_BlockedCenter_FourClip_FindsReferenceCount()
    {
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip);

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.Equal(15, result.ClusterCount);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle23x23_BlockedCenter_ThreeClip_FindsNearOptimal()
    {
        var grid = TemplateGenerator.Circle(diameter: 23, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, new SolverOptions { MaxTimeSeconds = 120 });

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 103, $"Expected >= 103 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle29x29_BlockedCenter_ThreeClip_FindsNearOptimal()
    {
        var grid = TemplateGenerator.Circle(diameter: 29, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, new SolverOptions { MaxTimeSeconds = 300 });

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 164, $"Expected >= 164 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle33x33_BlockedCenter_ThreeClip_FindsNearOptimal()
    {
        var grid = TemplateGenerator.Circle(diameter: 33, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, new SolverOptions { MaxTimeSeconds = 420 });

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 212, $"Expected >= 212 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle15x15_BlockedCenter_FourClip_FindsNearOptimal()
    {
        var grid = TemplateGenerator.Circle(diameter: 15, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip, new SolverOptions { MaxTimeSeconds = 60 });

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 27, $"Expected >= 27 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle17x17_BlockedCenter_FourClip_FindsNearOptimal()
    {
        var grid = TemplateGenerator.Circle(diameter: 17, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip, new SolverOptions { MaxTimeSeconds = 120 });

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 36, $"Expected >= 36 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle23x23_BlockedCenter_FourClip_FindsNearOptimal()
    {
        var grid = TemplateGenerator.Circle(diameter: 23, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip, new SolverOptions { MaxTimeSeconds = 300 });

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 60, $"Expected >= 60 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle5x5_BlockedCenter_FiveClip_FindsSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip);

        AssertSolutionValid(grid, TetrisType.FiveClip, result);
        Assert.True(result.ClusterCount >= 1);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle11x11_BlockedCenter_FiveClip_FindsSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip, new SolverOptions { MaxTimeSeconds = 60 });

        AssertSolutionValid(grid, TetrisType.FiveClip, result);
        Assert.True(result.ClusterCount >= 10);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle15x15_BlockedCenter_FiveClip_FindsNearOptimal()
    {
        var grid = TemplateGenerator.Circle(diameter: 15, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip, new SolverOptions { MaxTimeSeconds = 120 });

        AssertSolutionValid(grid, TetrisType.FiveClip, result);
        Assert.True(result.ClusterCount >= 30, $"Expected >= 30 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Rectangle8x8_ThreeClip_FindsOptimal()
    {
        var grid = TemplateGenerator.Rectangle(width: 8, height: 8);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip);

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 15, $"Expected >= 15 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Rectangle10x10_FourClip_FindsOptimal()
    {
        var grid = TemplateGenerator.Rectangle(width: 10, height: 10);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip);

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 13, $"Expected >= 13 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle11x11_OpenCenter_ThreeClip_FindsSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: false);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip);

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 18, $"Expected >= 18 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle15x15_OpenCenter_ThreeClip_FindsSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 15, blockCenter: false);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, new SolverOptions { MaxTimeSeconds = 60 });

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 43, $"Expected >= 43 clusters, got {result.ClusterCount}");
    }

    // ── ThreeClip blocked-center matrix ──────────────────────────────────

    [Fact]
    public void Solve_Circle7x7_BlockedCenter_ThreeClip_FindsSolution()
    {
        // 37 total cells, 36 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 7, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip);

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 7, $"Expected >= 7 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle9x9_BlockedCenter_ThreeClip_FindsSolution()
    {
        // 69 total cells, 68 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 9, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip);

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 14, $"Expected >= 14 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle11x11_BlockedCenter_ThreeClip_FindsSolution()
    {
        // 97 total cells, 96 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip);

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 20, $"Expected >= 20 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle13x13_BlockedCenter_ThreeClip_FindsSolution()
    {
        // 137 total cells, 136 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 13, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, new SolverOptions { MaxTimeSeconds = 60 });

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 28, $"Expected >= 28 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle19x19_BlockedCenter_ThreeClip_FindsSolution()
    {
        // 293 total cells, 292 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 19, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, new SolverOptions { MaxTimeSeconds = 120 });

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 62, $"Expected >= 62 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle21x21_BlockedCenter_ThreeClip_FindsSolution()
    {
        // 349 total cells, 348 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 21, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, new SolverOptions { MaxTimeSeconds = 120 });

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 73, $"Expected >= 73 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle25x25_BlockedCenter_ThreeClip_FindsSolution()
    {
        // 489 total cells, 488 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 25, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, new SolverOptions { MaxTimeSeconds = 300 });

        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert.True(result.ClusterCount >= 103, $"Expected >= 103 clusters, got {result.ClusterCount}");
    }

    // ── FourClip blocked-center matrix ───────────────────────────────────

    [Fact]
    public void Solve_Circle5x5_BlockedCenter_FourClip_FindsSolution()
    {
        // 21 total cells, 20 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip);

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 2, $"Expected >= 2 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle7x7_BlockedCenter_FourClip_FindsSolution()
    {
        // 37 total cells, 36 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 7, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip);

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 5, $"Expected >= 5 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle9x9_BlockedCenter_FourClip_FindsSolution()
    {
        // 69 total cells, 68 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 9, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip);

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 9, $"Expected >= 9 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle13x13_BlockedCenter_FourClip_FindsSolution()
    {
        // 137 total cells, 136 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 13, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip, new SolverOptions { MaxTimeSeconds = 60 });

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 19, $"Expected >= 19 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle19x19_BlockedCenter_FourClip_FindsSolution()
    {
        // 293 total cells, 292 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 19, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip, new SolverOptions { MaxTimeSeconds = 120 });

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 40, $"Expected >= 40 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle21x21_BlockedCenter_FourClip_FindsSolution()
    {
        // 349 total cells, 348 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 21, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip, new SolverOptions { MaxTimeSeconds = 120 });

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 48, $"Expected >= 48 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    [Trait("Category", "Benchmark")]
    public void Solve_Circle25x25_BlockedCenter_FourClip_FindsSolution()
    {
        // 489 total cells, 488 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 25, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FourClip, new SolverOptions { MaxTimeSeconds = 300 });

        AssertSolutionValid(grid, TetrisType.FourClip, result);
        Assert.True(result.ClusterCount >= 68, $"Expected >= 68 clusters, got {result.ClusterCount}");
    }

    // ── FiveClip blocked-center matrix (5–21 only) ──────────────────────

    [Fact]
    public void Solve_Circle7x7_BlockedCenter_FiveClip_FindsSolution()
    {
        // 37 total cells, 36 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 7, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip);

        AssertSolutionValid(grid, TetrisType.FiveClip, result);
        Assert.True(result.ClusterCount >= 5, $"Expected >= 5 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle9x9_BlockedCenter_FiveClip_FindsSolution()
    {
        // 69 total cells, 68 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 9, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip);

        AssertSolutionValid(grid, TetrisType.FiveClip, result);
        Assert.True(result.ClusterCount >= 9, $"Expected >= 9 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle13x13_BlockedCenter_FiveClip_FindsSolution()
    {
        // 137 total cells, 136 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 13, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip, new SolverOptions { MaxTimeSeconds = 60 });

        AssertSolutionValid(grid, TetrisType.FiveClip, result);
        Assert.True(result.ClusterCount >= 19, $"Expected >= 19 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle17x17_BlockedCenter_FiveClip_FindsSolution()
    {
        // 225 total cells, 224 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 17, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip, new SolverOptions { MaxTimeSeconds = 60 });

        AssertSolutionValid(grid, TetrisType.FiveClip, result);
        Assert.True(result.ClusterCount >= 31, $"Expected >= 31 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle19x19_BlockedCenter_FiveClip_FindsSolution()
    {
        // 293 total cells, 292 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 19, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip, new SolverOptions { MaxTimeSeconds = 120 });

        AssertSolutionValid(grid, TetrisType.FiveClip, result);
        Assert.True(result.ClusterCount >= 40, $"Expected >= 40 clusters, got {result.ClusterCount}");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Solve_Circle21x21_BlockedCenter_FiveClip_FindsSolution()
    {
        // 349 total cells, 348 available (blocked center)
        var grid = TemplateGenerator.Circle(diameter: 21, blockCenter: true);
        var solver = new TetrisSolver();
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip, new SolverOptions { MaxTimeSeconds = 120 });

        AssertSolutionValid(grid, TetrisType.FiveClip, result);
        Assert.True(result.ClusterCount >= 48, $"Expected >= 48 clusters, got {result.ClusterCount}");
    }

    [Fact]
    public void Solve_Circle5x5_BlockedCenter_ThreeClip_HardHSymmetry_ProducesMirroredSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions
        {
            SymmetryType = SymmetryType.HorizontalReflection,
            SymmetryMode = SymmetryMode.Hard
        };
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, opts);
        SolutionVisualizer.Print(_output, grid, TetrisType.ThreeClip, result);
        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        AssertHorizontalSymmetry(grid, TetrisType.ThreeClip, result);
        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (g.Height - 1 - r, c));
    }

    [Fact]
    public void Solve_Circle5x5_BlockedCenter_ThreeClip_HardVSymmetry_ProducesMirroredSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions
        {
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Hard
        };
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, opts);
        SolutionVisualizer.Print(_output, grid, TetrisType.ThreeClip, result);
        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        AssertVerticalSymmetry(grid, TetrisType.ThreeClip, result);
        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (r, g.Width - 1 - c));
    }

    [Fact]
    public void Solve_Circle7x7_BlockedCenter_FiveClip_HardHSymmetry_ProducesMirroredSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 7, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions
        {
            SymmetryType = SymmetryType.HorizontalReflection,
            SymmetryMode = SymmetryMode.Hard
        };
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip, opts);
        SolutionVisualizer.Print(_output, grid, TetrisType.FiveClip, result);
        AssertSolutionValid(grid, TetrisType.FiveClip, result);
        AssertHorizontalSymmetry(grid, TetrisType.FiveClip, result);
        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.FiveClip,
            result,
            static (r, c, g) => (g.Height - 1 - r, c));
    }

    [Fact]
    public void Solve_Circle7x7_BlockedCenter_FiveClip_HardVSymmetry_ProducesMirroredSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 7, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions
        {
            SymmetryType = SymmetryType.VerticalReflection,
            SymmetryMode = SymmetryMode.Hard
        };
        SolverResult result = solver.Solve(grid, TetrisType.FiveClip, opts);
        SolutionVisualizer.Print(_output, grid, TetrisType.FiveClip, result);
        AssertSolutionValid(grid, TetrisType.FiveClip, result);
        AssertVerticalSymmetry(grid, TetrisType.FiveClip, result);
        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.FiveClip,
            result,
            static (r, c, g) => (r, g.Width - 1 - c));
    }

    [Fact]
    public void Solve_Circle9x9_BlockedCenter_ThreeClip_HardRotation90_ProducesSymmetricSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 9, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions
        {
            SymmetryType = SymmetryType.Rotation90,
            SymmetryMode = SymmetryMode.Hard
        };
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, opts);
        SolutionVisualizer.Print(_output, grid, TetrisType.ThreeClip, result);
        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert90RotationalSymmetry(grid, TetrisType.ThreeClip, result);
        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (c, g.Height - 1 - r));
    }

    [Fact]
    public void Solve_Circle9x9_BlockedCenter_ThreeClip_HardBothReflection_ProducesSymmetricSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 9, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions
        {
            SymmetryType = SymmetryType.BothReflection,
            SymmetryMode = SymmetryMode.Hard
        };
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, opts);
        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        AssertHorizontalSymmetry(grid, TetrisType.ThreeClip, result);
        AssertVerticalSymmetry(grid, TetrisType.ThreeClip, result);
        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (g.Height - 1 - r, c));
        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (r, g.Width - 1 - c));
        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (g.Height - 1 - r, g.Width - 1 - c));
    }

    [Fact]
    public void Solve_NonSquareGrid_Rotation90_ThrowsArgumentException()
    {
        var grid = TemplateGenerator.Rectangle(width: 4, height: 5);
        var solver = new TetrisSolver();
        var opts = new SolverOptions
        {
            SymmetryType = SymmetryType.Rotation90,
            SymmetryMode = SymmetryMode.Hard
        };

        Assert.Throws<ArgumentException>(() => solver.Solve(grid, TetrisType.ThreeClip, opts));
    }

    [Fact]
    public void Solve_Circle11x11_BlockedCenter_ThreeClip_HardRotation180_ProducesSymmetricSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions
        {
            SymmetryType = SymmetryType.Rotation180,
            SymmetryMode = SymmetryMode.Hard
        };
        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, opts);
        SolutionVisualizer.Print(_output, grid, TetrisType.ThreeClip, result);
        AssertSolutionValid(grid, TetrisType.ThreeClip, result);
        Assert180RotationalSymmetry(grid, TetrisType.ThreeClip, result);
        SymmetryCellAssertions.AssertCellLevelSymmetry(
            grid,
            TetrisType.ThreeClip,
            result,
            static (r, c, g) => (g.Height - 1 - r, g.Width - 1 - c));
    }

    [Fact]
    public void Solve_Circle11x11_BlockedCenter_FourClip_SoftSymmetry_FindsAtLeastAsManyClustersAsHard()
    {
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: true);
        var solver = new TetrisSolver();

        var hardResult = solver.Solve(grid, TetrisType.FourClip, new SolverOptions
        {
            SymmetryType = SymmetryType.HorizontalReflection,
            SymmetryMode = SymmetryMode.Hard
        });
        SolutionVisualizer.Print(_output, grid, TetrisType.FourClip, hardResult);

        var softResult = solver.Solve(grid, TetrisType.FourClip, new SolverOptions
        {
            SymmetryType = SymmetryType.HorizontalReflection,
            SymmetryMode = SymmetryMode.Soft
        });
        SolutionVisualizer.Print(_output, grid, TetrisType.FourClip, softResult);

        Assert.True(softResult.ClusterCount >= hardResult.ClusterCount);
    }

    private static void AssertHorizontalSymmetry(Grid grid, TetrisType type, SolverResult result)
    {
        var placementSet = new HashSet<(int Row, int Col, int ShapeIndex)>(
            result.Placements.Select(p => (p.Row, p.Col, p.ShapeIndex)));

        foreach (var p in result.Placements)
        {
            int mirrorRow = grid.Height - 1 - p.Row;
            int mirrorShape = type switch
            {
                TetrisType.FourClip => 0,
                TetrisType.FiveClip => p.ShapeIndex switch { 1 => 3, 3 => 1, _ => p.ShapeIndex },
                _ => p.ShapeIndex switch { 0 => 2, 2 => 0, _ => p.ShapeIndex }
            };
            Assert.Contains((mirrorRow, p.Col, mirrorShape), placementSet);
        }
    }

    private static void AssertVerticalSymmetry(Grid grid, TetrisType type, SolverResult result)
    {
        var placementSet = new HashSet<(int Row, int Col, int ShapeIndex)>(
            result.Placements.Select(p => (p.Row, p.Col, p.ShapeIndex)));

        foreach (var p in result.Placements)
        {
            int mirrorCol = grid.Width - 1 - p.Col;
            int mirrorShape = type switch
            {
                TetrisType.FourClip => 0,
                TetrisType.FiveClip => p.ShapeIndex switch { 0 => 2, 2 => 0, _ => p.ShapeIndex },
                _ => p.ShapeIndex switch { 1 => 3, 3 => 1, _ => p.ShapeIndex }
            };
            Assert.Contains((p.Row, mirrorCol, mirrorShape), placementSet);
        }
    }

    private static void Assert90RotationalSymmetry(Grid grid, TetrisType type, SolverResult result)
    {
        var placementSet = new HashSet<(int Row, int Col, int ShapeIndex)>(
            result.Placements.Select(p => (p.Row, p.Col, p.ShapeIndex)));

        foreach (var p in result.Placements)
        {
            // 90° CW: (r,c) -> (c, H-1-r), shape: 0->1->2->3->0
            int rotR = p.Col;
            int rotC = grid.Height - 1 - p.Row;
            int rotShape = type switch
            {
                TetrisType.FourClip => 0,
                _ => (p.ShapeIndex + 1) % 4
            };

            Assert.Contains((rotR, rotC, rotShape), placementSet);
        }
    }

    private static void Assert180RotationalSymmetry(Grid grid, TetrisType type, SolverResult result)
    {
        var placementSet = new HashSet<(int Row, int Col, int ShapeIndex)>(
            result.Placements.Select(p => (p.Row, p.Col, p.ShapeIndex)));

        foreach (var p in result.Placements)
        {
            int rotR = grid.Height - 1 - p.Row;
            int rotC = grid.Width - 1 - p.Col;
            int rotShape = type switch
            {
                TetrisType.FourClip => 0,
                _ => p.ShapeIndex switch { 0 => 2, 2 => 0, 1 => 3, 3 => 1, _ => p.ShapeIndex }
            };

            Assert.Contains((rotR, rotC, rotShape), placementSet);
        }
    }

    // ───── Multi-solution tests ─────

    [Fact]
    public void Solve_MultiSolution_ReturnsDistinctSameBoundSolutions()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions { NumSolutions = 3, MaxTimeSeconds = 10 };

        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, opts);

        Assert.InRange(result.AllSolutions.Count, 1, 3);

        // All solutions have same cluster count
        int expectedCount = result.ClusterCount;
        foreach (var solution in result.AllSolutions)
            Assert.Equal(expectedCount, solution.Count);

        // All solutions are pairwise distinct
        for (int a = 0; a < result.AllSolutions.Count; a++)
        for (int b = a + 1; b < result.AllSolutions.Count; b++)
        {
            var setA = new HashSet<(int, int, int)>(
                result.AllSolutions[a].Select(p => (p.Row, p.Col, p.ShapeIndex)));
            var setB = new HashSet<(int, int, int)>(
                result.AllSolutions[b].Select(p => (p.Row, p.Col, p.ShapeIndex)));

            Assert.False(setA.SetEquals(setB), $"Solutions {a} and {b} are identical");
        }
    }

    [Fact]
    public void Solve_MultiSolution_FirstSolutionMatchesPlacements()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions { NumSolutions = 3, MaxTimeSeconds = 10 };

        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, opts);

        Assert.True(result.AllSolutions.Count >= 1);
        Assert.Equal(
            result.Placements.Select(p => (p.Row, p.Col, p.ShapeIndex)).ToList(),
            result.AllSolutions[0].Select(p => (p.Row, p.Col, p.ShapeIndex)).ToList());
    }

    [Fact]
    public void Solve_MultiSolution_AllSolutionsRespectGrid()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions { NumSolutions = 3, MaxTimeSeconds = 10 };
        var type = TetrisType.ThreeClip;

        SolverResult result = solver.Solve(grid, type, opts);

        var shapes = ClusterShape.GetShapes(type);
        foreach (var solution in result.AllSolutions)
        {
            // All placements in bounds and on available cells
            foreach (var p in solution)
            {
                Assert.InRange(p.ShapeIndex, 0, shapes.Count - 1);
                foreach (var offset in shapes[p.ShapeIndex].Offsets)
                {
                    int r = p.Row + offset.DeltaRow;
                    int c = p.Col + offset.DeltaCol;
                    Assert.True(grid.IsInBounds(r, c), $"Cell ({r},{c}) out of bounds");
                    Assert.True(grid.IsAvailable(r, c), $"Cell ({r},{c}) is blocked");
                }
            }

            // No overlap on loader/clip cells
            var occupied = new HashSet<(int, int)>();
            foreach (var p in solution)
            {
                foreach (var offset in shapes[p.ShapeIndex].Offsets)
                {
                    if (offset.Role == CellRole.Connection)
                        continue;
                    var cell = (p.Row + offset.DeltaRow, p.Col + offset.DeltaCol);
                    Assert.True(occupied.Add(cell), $"Duplicate exclusive cell {cell}");
                }
            }
        }
    }

    [Fact]
    public void Solve_NumSolutions1_ReturnsSingleSolution()
    {
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions { NumSolutions = 1, MaxTimeSeconds = 10 };

        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, opts);

        Assert.Single(result.AllSolutions);
        Assert.Equal(
            result.Placements.Select(p => (p.Row, p.Col, p.ShapeIndex)).ToList(),
            result.AllSolutions[0].Select(p => (p.Row, p.Col, p.ShapeIndex)).ToList());
    }

    [Fact]
    public void Solve_MultiSolution_OptimalTerminatedByUnsat_YieldsMultipleEnumeratedSolutions()
    {
        // 5x5 circle (blocked center): 20 cells, 4 threeClips × 4 cells = 16, optimal bestEmpty=4.
        // Phase 2 descent goes bound=0 → UNSAT (proves optimal). This exercises the bug path
        // where the retained solver was being disposed before UNSAT-termination.
        var grid = TemplateGenerator.Circle(diameter: 5, blockCenter: true);
        var solver = new TetrisSolver();
        var opts = new SolverOptions { NumSolutions = 5, MaxTimeSeconds = 10 };

        SolverResult result = solver.Solve(grid, TetrisType.ThreeClip, opts);

        Assert.Equal(SolverStatus.Optimal, result.Status);
        Assert.True(result.AllSolutions.Count >= 2,
            $"Expected >1 enumerated solutions after UNSAT-proven optimal; got {result.AllSolutions.Count}.");
    }
}