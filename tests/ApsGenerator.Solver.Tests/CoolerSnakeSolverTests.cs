using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Cooler;

namespace ApsGenerator.Solver.Tests;

public sealed class CoolerSnakeSolverTests
{
    private static CoolerSnakeOptions FastOptions() => new()
    {
        MaxTimeSeconds = 15,
        Threads = 1,
    };

    [Fact]
    public void SingleThreeClip_WithOpenArm_VerticalGivesZeroTopIntakes()
    {
        var grid = TemplateGenerator.Rectangle(3, 3);
        var placements = new[] { new Placement(1, 1, ShapeIndex: 3) };
        var result = new CoolerSnakeSolver().Solve(grid, TetrisType.ThreeClip, placements, FastOptions());

        Assert.Equal(CoolerSnakeStatus.Sat, result.Status);
        Assert.True(result.IntakesMeetQuota);
        Assert.Contains(result.EjectorDirs, e => e.Kind == EjectorKind.VerticalOpenArmDown);
        Assert.DoesNotContain(result.IntakeCells, i => !i.IsUnderneath);
        Assert.Equal(4, result.IntakeCells.Count(i => i.IsUnderneath));
        Assert.All(result.CoolerCells, c => Assert.Equal(0, c.SnakeId));
    }

    [Fact]
    public void TwoSectors_BlockedColumn_IndependentSnakes()
    {
        // 7x3 with a full blocked center column → two Available sectors.
        var grid = TemplateGenerator.Rectangle(7, 3);
        const int blockedCol = 3;
        for (int r = 0; r < grid.Height; r++)
            grid[r, blockedCol] = CellState.Blocked;

        var placements = new[]
        {
            new Placement(1, 1, ShapeIndex: 3), // left sector, open right
            new Placement(1, 5, ShapeIndex: 1), // right sector, open left
        };

        var result = new CoolerSnakeSolver().Solve(
            grid, TetrisType.ThreeClip, placements, FastOptions());

        Assert.Equal(CoolerSnakeStatus.Sat, result.Status);
        Assert.True(result.IntakesMeetQuota);
        Assert.Equal(2, result.EjectorDirs.Count);
        Assert.Contains(result.EjectorDirs, e => e.ClusterIndex == 0);
        Assert.Contains(result.EjectorDirs, e => e.ClusterIndex == 1);

        var snakeIds = result.CoolerCells.Select(c => c.SnakeId).Distinct().OrderBy(id => id).ToList();
        Assert.Equal([0, 1], snakeIds);
        Assert.DoesNotContain(result.CoolerCells, c => c.Col == blockedCol);
        Assert.Contains("2 sectors", result.Detail);
    }

    [Fact]
    public void OmitEjectors_ThreeClip_SatWithEmptyEjectorsAndNoTopIntakes()
    {
        var grid = TemplateGenerator.Rectangle(3, 3);
        var placements = new[] { new Placement(1, 1, ShapeIndex: 3) };
        var options = new CoolerSnakeOptions
        {
            MaxTimeSeconds = 15,
            Threads = 1,
            OmitEjectors = true,
        };
        var result = new CoolerSnakeSolver().Solve(grid, TetrisType.ThreeClip, placements, options);

        Assert.Equal(CoolerSnakeStatus.Sat, result.Status);
        Assert.Empty(result.EjectorDirs);
        Assert.DoesNotContain(result.IntakeCells, i => !i.IsUnderneath);
        Assert.Equal(4, result.IntakeCells.Count(i => i.IsUnderneath));
        Assert.True(result.CoolerCells.Count > 0);
    }

    [Fact]
    public void LoaderChaining_TwoAdjacentLoaders_AttachOnce()
    {
        // Two 3-clips with adjacent loaders on a wider grid.
        var grid = TemplateGenerator.Rectangle(5, 3);
        var placements = new[]
        {
            new Placement(1, 1, ShapeIndex: 3), // open right
            new Placement(1, 2, ShapeIndex: 1), // open left — loaders at (1,1) and (1,2) adjacent
        };
        var packing = PackingAnalyzer.Analyze(grid, TetrisType.ThreeClip, placements);
        Assert.Single(packing.LoaderChains);
        Assert.Equal(2, packing.LoaderChains[0].Count);

        var result = new CoolerSnakeSolver().Solve(grid, TetrisType.ThreeClip, placements, FastOptions());
        Assert.Equal(CoolerSnakeStatus.Sat, result.Status);
        Assert.True(result.CoolerCells.Count >= 1);
    }

    [Fact]
    public void FourClip_SingleCluster_MeetsQuotaFive()
    {
        var grid = TemplateGenerator.Rectangle(3, 3);
        var placements = new[] { new Placement(1, 1, ShapeIndex: 0) };
        var result = new CoolerSnakeSolver().Solve(grid, TetrisType.FourClip, placements, FastOptions());

        Assert.Equal(CoolerSnakeStatus.Sat, result.Status);
        Assert.Equal(5, result.RequiredIntakesPerCluster);
        Assert.True(result.IntakesMeetQuota);
        Assert.All(result.EjectorDirs, e => Assert.Equal(EjectorKind.Bottom, e.Kind));
    }

    [Fact]
    public void GapRouting_EmptyBypass_AllowsSnake()
    {
        var left = new ClusterInfo
        {
            Index = 0,
            Loader = new CellKey(1, 0),
            Clips = [new CellKey(0, 0), new CellKey(2, 0), new CellKey(1, -1)],
            Footprint = [new CellKey(1, 0), new CellKey(0, 0), new CellKey(2, 0), new CellKey(1, -1)],
            OpenArmDelta = (0, 1),
        };
        var right = new ClusterInfo
        {
            Index = 1,
            Loader = new CellKey(1, 2),
            Clips = [new CellKey(0, 2), new CellKey(2, 2), new CellKey(1, 3)],
            Footprint = [new CellKey(1, 2), new CellKey(0, 2), new CellKey(2, 2), new CellKey(1, 3)],
            OpenArmDelta = (0, -1),
        };
        var packing = PackingAnalyzer.FromHandcrafted(TetrisType.ThreeClip, [left, right]);
        packing = DomainExtend.AddBypassCell(packing, new CellKey(1, 1));

        var result = new CoolerSnakeSolver().Solve(
            packing,
            new CoolerSnakeOptions { MaxTimeSeconds = 10 },
            grid: null);

        Assert.Equal(CoolerSnakeStatus.Sat, result.Status);
        Assert.Contains(result.CoolerCells, c => c.Row == 1 && c.Col == 1 && c.IsGap);
    }

    [Fact]
    public void TopIntakes_AvoidLoadersWhenClipsSuffice_AndNeverBothInDoubleCluster()
    {
        var grid = TemplateGenerator.Rectangle(5, 3);
        var placements = new[]
        {
            new Placement(1, 1, ShapeIndex: 3),
            new Placement(1, 2, ShapeIndex: 1),
        };
        var packing = PackingAnalyzer.Analyze(grid, TetrisType.ThreeClip, placements);
        Assert.True(packing.LoaderChains.Count >= 1);
        Assert.True(packing.LoaderChains[0].Count >= 2);

        var result = new CoolerSnakeSolver().Solve(grid, TetrisType.ThreeClip, placements, FastOptions());
        Assert.Equal(CoolerSnakeStatus.Sat, result.Status);
        Assert.True(result.IntakesMeetQuota);

        var loaderSet = packing.LoaderCells.ToHashSet();
        var toppedLoaders = result.IntakeCells
            .Where(i => !i.IsUnderneath && loaderSet.Contains(new CellKey(i.Row, i.Col)))
            .Select(i => new CellKey(i.Row, i.Col))
            .ToHashSet();

        // 3-clip with bottom ejectors: 2 clips free on bottom → top deficit 2, 3 clips available → no loader tops.
        Assert.Empty(toppedLoaders);

        // Even if some path allowed one loader top, never top both adjacent loaders.
        for (int a = 0; a < packing.LoaderCells.Count; a++)
        {
            for (int b = a + 1; b < packing.LoaderCells.Count; b++)
            {
                var la = packing.LoaderCells[a];
                var lb = packing.LoaderCells[b];
                if (Math.Abs(la.Row - lb.Row) + Math.Abs(la.Col - lb.Col) != 1)
                    continue;
                Assert.False(toppedLoaders.Contains(la) && toppedLoaders.Contains(lb));
            }
        }
    }

    [Fact]
    public void IntakeBridge_ReconnectsWhenTopIntakesCutDeckPath()
    {
        // Two clusters linked only by corridor cells (1,1)-(1,2). Deterministic intake
        // placement with k=3 occupies those corridor cells; local bridges must reconnect.
        var left = new ClusterInfo
        {
            Index = 0,
            Loader = new CellKey(1, 0),
            Clips = [new CellKey(0, 0), new CellKey(2, 0), new CellKey(1, 1)],
            Footprint = [new CellKey(1, 0), new CellKey(0, 0), new CellKey(2, 0), new CellKey(1, 1)],
            OpenArmDelta = (0, 1),
        };
        var right = new ClusterInfo
        {
            Index = 1,
            Loader = new CellKey(1, 3),
            Clips = [new CellKey(0, 3), new CellKey(2, 3), new CellKey(1, 2)],
            Footprint = [new CellKey(1, 3), new CellKey(0, 3), new CellKey(2, 3), new CellKey(1, 2)],
            OpenArmDelta = (0, -1),
        };
        var packing = PackingAnalyzer.FromHandcrafted(TetrisType.ThreeClip, [left, right]);
        int n = packing.FootprintCells.Count;
        var indexOf = packing.FootprintCells
            .Select((c, i) => (c, i))
            .ToDictionary(x => x.c, x => x.i);
        var neighbors = CoolerGraph.BuildNeighbors(packing.FootprintCells, indexOf);

        // Force corridor intakes (same as non-loader-first placement with k=3).
        var intakeMask = new bool[n];
        foreach (var cell in new[]
                 {
                     new CellKey(0, 0), new CellKey(2, 0), new CellKey(1, 1),
                     new CellKey(0, 3), new CellKey(2, 3), new CellKey(1, 2),
                 })
            intakeMask[indexOf[cell]] = true;

        var active = Enumerable.Range(0, n).Where(i => !intakeMask[i]).ToHashSet();
        var terminals = new List<int> { indexOf[new CellKey(1, 0)], indexOf[new CellKey(1, 3)] };
        Assert.False(CoolerGraph.SteinerTree(active, neighbors, terminals, out _));

        Assert.True(CoolerBridgeGraph.SteinerTreeWithIntakeBridges(
            n, intakeMask, neighbors, terminals, out var deck, out var bridge));
        Assert.True(bridge.Count >= 1);
        Assert.True(bridge.Count < n);
        Assert.True(deck.Count >= 1);
        Assert.Contains(bridge, i =>
        {
            var c = packing.FootprintCells[i];
            return c.Row == 1 && (c.Col == 1 || c.Col == 2);
        });
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Circle45_BothReflectionHard_LocalBridgesMeetQuotaWithoutLoaderTops()
    {
        // Hostile pack that needs local over-intake bridges.
        var grid = TemplateGenerator.Circle(diameter: 45, blockCenter: false);
        var tetris = new TetrisSolver().Solve(grid, TetrisType.ThreeClip, new SolverOptions
        {
            MaxTimeSeconds = 120,
            EarlyStopEnabled = false,
            SymmetryType = SymmetryType.BothReflection,
            SymmetryMode = SymmetryMode.Hard,
            NumSolutions = 1,
        });
        Assert.Equal(SolverStatus.Optimal, tetris.Status);

        var packing = PackingAnalyzer.Analyze(grid, TetrisType.ThreeClip, tetris.Placements);
        var result = new CoolerSnakeSolver().Solve(
            grid,
            TetrisType.ThreeClip,
            tetris.Placements,
            new CoolerSnakeOptions
            {
                MaxTimeSeconds = 120,
                Threads = Math.Max(1, Environment.ProcessorCount - 1),
            });

        Assert.Equal(CoolerSnakeStatus.Sat, result.Status);
        Assert.Equal(CoolerStageDetails.ElevatedBridges, result.Detail);
        Assert.True(result.IntakesMeetQuota);
        Assert.Equal(2, result.LayersUsed);

        int bridge = result.CoolerCells.Count(c => c.Layer >= 1);
        int deck = result.CoolerCells.Count(c => c.Layer <= 0);
        Assert.True(deck >= 1);
        Assert.True(bridge >= 1);
        Assert.True(bridge < packing.FootprintCells.Count / 2);

        var loaderSet = packing.LoaderCells.ToHashSet();
        Assert.DoesNotContain(
            result.IntakeCells,
            i => !i.IsUnderneath && loaderSet.Contains(new CellKey(i.Row, i.Col)));
    }

    [Fact]
    public void CoolerOpenFaces_PresentOnSnakeCells()
    {
        var grid = TemplateGenerator.Rectangle(3, 3);
        var placements = new[] { new Placement(1, 1, ShapeIndex: 0) };
        var result = new CoolerSnakeSolver().Solve(grid, TetrisType.ThreeClip, placements, FastOptions());

        Assert.Equal(CoolerSnakeStatus.Sat, result.Status);
        Assert.NotEmpty(result.CoolerCells);
        Assert.All(
            result.CoolerCells,
            c => Assert.Equal(
                c.OpenFaces,
                c.OpenFaces & (CoolerFaceFlags.North | CoolerFaceFlags.East | CoolerFaceFlags.South | CoolerFaceFlags.West)));
    }

    [Fact]
    public void SmallCircle_ThreeClip_SatWithinBudget()
    {
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: false);
        var tetris = new TetrisSolver().Solve(grid, TetrisType.ThreeClip, new SolverOptions
        {
            MaxTimeSeconds = 30,
            EarlyStopEnabled = false,
        });
        Assert.Equal(SolverStatus.Optimal, tetris.Status);

        var result = new CoolerSnakeSolver().Solve(
            grid, TetrisType.ThreeClip, tetris.Placements, FastOptions());

        Assert.Equal(CoolerSnakeStatus.Sat, result.Status);
        Assert.True(result.IntakesMeetQuota);
        Assert.True(result.SolveSeconds < 15);
    }

    [Fact]
    public void FiveClip_SteinerSmoke()
    {
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: false);
        var tetris = new TetrisSolver().Solve(grid, TetrisType.FiveClip, new SolverOptions
        {
            MaxTimeSeconds = 30,
            EarlyStopEnabled = false,
        });
        Assert.True(tetris.ClusterCount >= 1);

        var result = new CoolerSnakeSolver().Solve(
            grid, TetrisType.FiveClip, tetris.Placements, FastOptions());

        Assert.Equal(CoolerSnakeStatus.Sat, result.Status);

        var shapes = ClusterShape.GetShapes(TetrisType.FiveClip);
        var connections = new HashSet<(int Row, int Col)>();
        foreach (var p in tetris.Placements)
        {
            foreach (var o in shapes[p.ShapeIndex].Offsets)
            {
                if (o.Role == CellRole.Connection)
                    connections.Add((p.Row + o.DeltaRow, p.Col + o.DeltaCol));
            }
        }

        Assert.True(connections.Count >= 1);
        Assert.True(result.CoolerCells.Count >= connections.Count);
        Assert.Equal(connections.Count, result.CoolerCells.Count(c => c.ConnectDown));
        Assert.All(
            result.CoolerCells.Where(c => c.ConnectDown),
            c => Assert.Contains((c.Row, c.Col), connections));

        // Prefer empties ∪ shafts — should not pave most of the exclusive footprint.
        int exclusiveCount = tetris.Placements.Sum(p => shapes[p.ShapeIndex].Offsets.Count);
        Assert.True(
            result.CoolerCells.Count <= connections.Count + exclusiveCount / 2,
            $"snake too dense: {result.CoolerCells.Count} cells for {connections.Count} shafts");
    }

    [Fact]
    public void IntakeAssign_CorridorPenalty_PrefersLeafIntakes()
    {
        var left = new ClusterInfo
        {
            Index = 0,
            Loader = new CellKey(1, 0),
            Clips = [new CellKey(0, 0), new CellKey(2, 0), new CellKey(1, 1)],
            Footprint = [new CellKey(1, 0), new CellKey(0, 0), new CellKey(2, 0), new CellKey(1, 1)],
            OpenArmDelta = (0, 1),
        };
        var right = new ClusterInfo
        {
            Index = 1,
            Loader = new CellKey(1, 3),
            Clips = [new CellKey(0, 3), new CellKey(2, 3), new CellKey(1, 2)],
            Footprint = [new CellKey(1, 3), new CellKey(0, 3), new CellKey(2, 3), new CellKey(1, 2)],
            OpenArmDelta = (0, -1),
        };
        var packing = PackingAnalyzer.FromHandcrafted(TetrisType.ThreeClip, [left, right]);
        var ctx = CoolerRoutingContext.From(packing);
        var catalog = EjectorCatalog.Build(packing, grid: null);
        var assignment = catalog.Select(c => c.OrderBy(x => x.TopDeficit).First()).ToList();
        var byCluster = assignment.ToDictionary(a => a.ClusterIndex);

        var mask = new bool[ctx.CellCount];
        Assert.True(CoolerIntakeAssign.TryAssignMinimizingComponents(ctx, byCluster, mask));

        Assert.False(mask[ctx.IndexOf[new CellKey(1, 1)]]);
        Assert.False(mask[ctx.IndexOf[new CellKey(1, 2)]]);
        Assert.Equal(1, CoolerIntakeAssign.CountTerminalComponents(ctx, mask));
    }

    [Fact]
    public void BridgeRefine_MovesCorridorIntakeOntoFreeLeaf_ClearsBridge()
    {
        var left = new ClusterInfo
        {
            Index = 0,
            Loader = new CellKey(1, 0),
            Clips = [new CellKey(0, 0), new CellKey(2, 0), new CellKey(1, 1)],
            Footprint = [new CellKey(1, 0), new CellKey(0, 0), new CellKey(2, 0), new CellKey(1, 1)],
            OpenArmDelta = (0, 1),
        };
        var right = new ClusterInfo
        {
            Index = 1,
            Loader = new CellKey(1, 3),
            Clips = [new CellKey(0, 3), new CellKey(2, 3), new CellKey(1, 2)],
            Footprint = [new CellKey(1, 3), new CellKey(0, 3), new CellKey(2, 3), new CellKey(1, 2)],
            OpenArmDelta = (0, -1),
        };
        var packing = PackingAnalyzer.FromHandcrafted(TetrisType.ThreeClip, [left, right]);
        var ctx = CoolerRoutingContext.From(packing);
        var catalog = EjectorCatalog.Build(packing, grid: null);
        var assignment = catalog.Select(c => c.OrderBy(x => x.TopDeficit).First()).ToList();

        var mask = new bool[ctx.CellCount];
        mask[ctx.IndexOf[new CellKey(1, 1)]] = true;
        mask[ctx.IndexOf[new CellKey(0, 0)]] = true;
        mask[ctx.IndexOf[new CellKey(1, 2)]] = true;
        mask[ctx.IndexOf[new CellKey(0, 3)]] = true;

        var bridged = CoolerGraph.TrySteinerDeckOrBridge(ctx, packing, assignment, mask);
        Assert.NotNull(bridged);
        Assert.Equal(CoolerStageDetails.ElevatedBridges, bridged!.Detail);
        Assert.True(bridged.BridgeCoolerCells.Count > 0);

        var refined = BridgeRefine.Improve(ctx, packing, assignment, bridged, mask);
        Assert.True(
            refined.BridgeCoolerCells.Count < bridged.BridgeCoolerCells.Count
            || refined.BridgeCoolerCells.Count == 0);
    }
}
