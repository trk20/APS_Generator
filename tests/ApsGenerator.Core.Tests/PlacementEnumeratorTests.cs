using ApsGenerator.Core;
using ApsGenerator.Core.Models;

namespace ApsGenerator.Core.Tests;

public sealed class PlacementEnumeratorTests
{
    [Fact]
    public void Grid3x3_AllAvailable_FourClip_HasExactlyOneCenteredPlacement()
    {
        var grid = new Grid(width: 3, height: 3);

        var placements = PlacementEnumerator.Enumerate(grid, TetrisType.FourClip);

        Assert.Single(placements);
        Assert.Equal(new Placement(Row: 1, Col: 1, ShapeIndex: 0), placements[0]);
    }

    [Fact]
    public void Grid3x3_AllAvailable_ThreeClip_HasEightValidPlacements()
    {
        var grid = new Grid(width: 3, height: 3);

        var placements = PlacementEnumerator.Enumerate(grid, TetrisType.ThreeClip);

        Assert.Equal(8, placements.Count);
        Assert.All(placements, placement => AssertPlacementFootprintIsValid(grid, TetrisType.ThreeClip, placement));
    }

    [Fact]
    public void Grid3x3_BlockedCenter_FourClip_HasNoPlacements()
    {
        var grid = new Grid(width: 3, height: 3);
        grid[1, 1] = CellState.Blocked;

        var placements = PlacementEnumerator.Enumerate(grid, TetrisType.FourClip);

        Assert.Empty(placements);
    }

    [Fact]
    public void Grid5x5_AllAvailable_FourClip_HasPlacementsAndAllAreValid()
    {
        var grid = new Grid(width: 5, height: 5);

        var placements = PlacementEnumerator.Enumerate(grid, TetrisType.FourClip);

        Assert.NotEmpty(placements);
        Assert.All(placements, placement => AssertPlacementFootprintIsValid(grid, TetrisType.FourClip, placement));
    }

    [Theory]
    [InlineData(TetrisType.ThreeClip)]
    [InlineData(TetrisType.FourClip)]
    [InlineData(TetrisType.FiveClip)]
    public void AllReturnedPlacements_HaveValidFootprints(TetrisType type)
    {
        var grid = TemplateGenerator.Circle(diameter: 11, blockCenter: true);
        var placements = PlacementEnumerator.Enumerate(grid, type);

        Assert.All(placements, placement => AssertPlacementFootprintIsValid(grid, type, placement));
    }

    private static void AssertPlacementFootprintIsValid(Grid grid, TetrisType type, Placement placement)
    {
        var shapes = ClusterShape.GetShapes(type);
        Assert.InRange(placement.ShapeIndex, 0, shapes.Count - 1);

        var offsets = shapes[placement.ShapeIndex].Offsets;
        foreach (var offset in offsets)
        {
            int row = placement.Row + offset.DeltaRow;
            int col = placement.Col + offset.DeltaCol;

            Assert.True(grid.IsInBounds(row, col));
            Assert.True(grid.IsAvailable(row, col));
        }
    }
}