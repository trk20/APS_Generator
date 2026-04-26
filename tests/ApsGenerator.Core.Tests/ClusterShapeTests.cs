using ApsGenerator.Core.Models;

namespace ApsGenerator.Core.Tests;

public sealed class ClusterShapeTests
{
    [Fact]
    public void ThreeClip_ProducesExactlyFourShapes()
    {
        var shapes = ClusterShape.GetShapes(TetrisType.ThreeClip);

        Assert.Equal(4, shapes.Count);
    }

    [Fact]
    public void FourClip_ProducesExactlyOneShape()
    {
        var shapes = ClusterShape.GetShapes(TetrisType.FourClip);

        Assert.Single(shapes);
    }

    [Fact]
    public void FiveClip_ProducesExactlyFourShapes()
    {
        var shapes = ClusterShape.GetShapes(TetrisType.FiveClip);

        Assert.Equal(4, shapes.Count);
    }

    [Fact]
    public void ThreeClip_EachShapeHasExactlyFourOffsets()
    {
        var shapes = ClusterShape.GetShapes(TetrisType.ThreeClip);

        foreach (var shape in shapes)
            Assert.Equal(4, shape.Offsets.Count);
    }

    [Fact]
    public void FourClip_EachShapeHasExactlyFiveOffsets()
    {
        var shapes = ClusterShape.GetShapes(TetrisType.FourClip);

        foreach (var shape in shapes)
            Assert.Equal(5, shape.Offsets.Count);
    }

    [Fact]
    public void FiveClip_EachShapeHasExactlyFiveOffsets()
    {
        var shapes = ClusterShape.GetShapes(TetrisType.FiveClip);

        foreach (var shape in shapes)
            Assert.Equal(5, shape.Offsets.Count);
    }

    [Theory]
    [InlineData(TetrisType.ThreeClip)]
    [InlineData(TetrisType.FourClip)]
    [InlineData(TetrisType.FiveClip)]
    public void EveryShape_HasExactlyOneLoaderAtOrigin(TetrisType type)
    {
        var shapes = ClusterShape.GetShapes(type);

        foreach (var shape in shapes)
        {
            var loaders = shape.Offsets.Where(x => x.Role == CellRole.Loader).ToList();

            Assert.Single(loaders);
            Assert.Equal(0, loaders[0].DeltaRow);
            Assert.Equal(0, loaders[0].DeltaCol);
        }
    }

    [Theory]
    [InlineData(TetrisType.ThreeClip)]
    [InlineData(TetrisType.FourClip)]
    public void ThreeClipAndFourClip_HaveNoConnectionRoleCells(TetrisType type)
    {
        var shapes = ClusterShape.GetShapes(type);

        foreach (var shape in shapes)
            Assert.DoesNotContain(shape.Offsets, offset => offset.Role == CellRole.Connection);
    }

    [Fact]
    public void FiveClip_EachShapeHasExactlyOneConnectionRoleCell()
    {
        var shapes = ClusterShape.GetShapes(TetrisType.FiveClip);

        foreach (var shape in shapes)
        {
            var connectionCount = shape.Offsets.Count(offset => offset.Role == CellRole.Connection);
            Assert.Equal(1, connectionCount);
        }
    }
}