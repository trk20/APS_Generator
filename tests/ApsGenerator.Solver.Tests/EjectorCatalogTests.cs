using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Cooler;

namespace ApsGenerator.Solver.Tests;

public sealed class EjectorCatalogTests
{
    [Fact]
    public void ThreeClip_OpenArmFree_IncludesVerticalDownWithZeroTopDeficit()
    {
        // Loader at (1,1), open right → arm (1,2) empty on a 3x3 with one 3-clip rot3.
        var grid = TemplateGenerator.Rectangle(3, 3);
        var placements = new[] { new Placement(1, 1, ShapeIndex: 3) };
        var packing = PackingAnalyzer.Analyze(grid, TetrisType.ThreeClip, placements);
        var catalog = EjectorCatalog.Build(packing, grid);

        Assert.Single(catalog);
        var vertical = catalog[0].Where(c => c.Kind == EjectorKind.VerticalOpenArmDown).ToList();
        Assert.Single(vertical);
        Assert.Equal(0, vertical[0].TopDeficit);
        Assert.Equal(4, vertical[0].BottomIntakeCells.Count);
        Assert.Contains(vertical[0].BottomIntakeCells, c => c.Equals(packing.Clusters[0].Loader));
    }

    [Fact]
    public void ThreeClip_OpenArmBlocked_OmitsVertical()
    {
        var grid = TemplateGenerator.Rectangle(3, 3);
        grid[1, 2] = CellState.Blocked; // open-right arm of rot3 at (1,1)
        var placements = new[] { new Placement(1, 1, ShapeIndex: 3) };
        var packing = PackingAnalyzer.Analyze(grid, TetrisType.ThreeClip, placements);
        var catalog = EjectorCatalog.Build(packing, grid);

        Assert.DoesNotContain(catalog[0], c => c.Kind == EjectorKind.VerticalOpenArmDown);
        Assert.All(catalog[0], c => Assert.Equal(EjectorKind.Bottom, c.Kind));
        Assert.All(catalog[0], c => Assert.Equal(2, c.TopDeficit));
    }

    [Fact]
    public void FourClip_PlusShape_OnlyBottomCandidates()
    {
        var grid = TemplateGenerator.Rectangle(5, 5);
        var placements = new[] { new Placement(2, 2, ShapeIndex: 0) };
        var packing = PackingAnalyzer.Analyze(grid, TetrisType.FourClip, placements);
        var catalog = EjectorCatalog.Build(packing, grid);

        Assert.All(catalog[0], c => Assert.Equal(EjectorKind.Bottom, c.Kind));
        Assert.Equal(4, catalog[0].Count);
    }

    [Fact]
    public void BottomCandidate_ExcludesProtrudedClipFromBottomIntakes()
    {
        var cluster = new ClusterInfo
        {
            Index = 0,
            Loader = new CellKey(1, 1),
            Clips =
            [
                new CellKey(0, 1),
                new CellKey(1, 0),
                new CellKey(1, 2),
            ],
            Footprint =
            [
                new CellKey(1, 1),
                new CellKey(0, 1),
                new CellKey(1, 0),
                new CellKey(1, 2),
            ],
            OpenArmDelta = (1, 0),
        };
        var packing = PackingAnalyzer.FromHandcrafted(TetrisType.ThreeClip, [cluster]);
        var catalog = EjectorCatalog.Build(packing, grid: null);
        var bottom = catalog[0].First(c => c.Kind == EjectorKind.Bottom);
        Assert.DoesNotContain(bottom.BottomIntakeCells, c => c.Equals(bottom.Protrusion));
        Assert.Equal(2, bottom.TopDeficit);
    }

    [Fact]
    public void BuildIntakeOnly_ThreeClip_ZeroTopDeficitAndLoaderPlusClips()
    {
        var grid = TemplateGenerator.Rectangle(3, 3);
        var placements = new[] { new Placement(1, 1, ShapeIndex: 3) };
        var packing = PackingAnalyzer.Analyze(grid, TetrisType.ThreeClip, placements);
        var catalog = EjectorCatalog.BuildIntakeOnly(packing);

        Assert.Single(catalog);
        Assert.Single(catalog[0]);
        var only = catalog[0][0];
        Assert.Equal(EjectorKind.None, only.Kind);
        Assert.Equal(0, only.TopDeficit);
        Assert.Equal(4, only.BottomIntakeCells.Count);
        Assert.Contains(only.BottomIntakeCells, c => c.Equals(packing.Clusters[0].Loader));
    }

    [Fact]
    public void BuildIntakeOnly_FourClip_ZeroTopDeficit()
    {
        var grid = TemplateGenerator.Rectangle(5, 5);
        var placements = new[] { new Placement(2, 2, ShapeIndex: 0) };
        var packing = PackingAnalyzer.Analyze(grid, TetrisType.FourClip, placements);
        var catalog = EjectorCatalog.BuildIntakeOnly(packing);

        Assert.Single(catalog[0]);
        Assert.Equal(0, catalog[0][0].TopDeficit);
        Assert.Equal(5, catalog[0][0].BottomIntakeCells.Count);
    }
}
