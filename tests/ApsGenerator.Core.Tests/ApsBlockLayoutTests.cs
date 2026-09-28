using System.Numerics;
using ApsGenerator.Core.Export;
using ApsGenerator.Core.Models;

namespace ApsGenerator.Core.Tests;

public sealed class ApsBlockLayoutTests
{
    public static IEnumerable<object[]> Shapes() => Enum.GetValues<TetrisType>()
        .SelectMany(type => Enumerable.Range(0, ClusterShape.GetShapes(type).Count).Select(shape => new object[] { type, shape }));

    [Theory]
    [MemberData(nameof(Shapes))]
    public void NativeClipsPointAtTheirLoaderForEveryShape(TetrisType type, int shapeIndex)
    {
        var grid = TemplateGenerator.Rectangle(9, 9);
        ClusterShape shape = ClusterShape.GetShapes(type)[shapeIndex];
        int height = type == TetrisType.FiveClip ? 6 : 2;
        var blocks = ApsBlockLayout.Build([new(4, 4, shapeIndex)], grid, type,
            new(height, ExportExtraLayers.TetrisOnly), BlockOrientationConvention.NativePrefab);
        CellOffset loader = shape.Offsets.Single(offset => offset.Role == CellRole.Loader);
        foreach (CellOffset clip in shape.Offsets.Where(offset => offset.Role == CellRole.Clip))
        {
            int y = type == TetrisType.FiveClip ? 1 : 0;
            LayoutBlock block = blocks[(4 + clip.DeltaCol, y, 4 - clip.DeltaRow)];
            var towardLoader = new Vector3(loader.DeltaCol - clip.DeltaCol, 0, clip.DeltaRow - loader.DeltaRow);
            Assert.Equal(towardLoader, BlockRotation.TransformDirection(block.RotationCode, -Vector3.UnitY));
            Assert.Equal(Vector3.UnitY, BlockRotation.TransformDirection(block.RotationCode, Vector3.UnitZ));
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(24)]
    public void NativeFiveClipStackPreservesVerticalClipAndIntakeRoll(int height)
    {
        var blocks = ApsBlockLayout.Build([new(4, 4, 0)], TemplateGenerator.Rectangle(9, 9), TetrisType.FiveClip,
            new(height, ExportExtraLayers.TetrisOnly), BlockOrientationConvention.NativePrefab);
        for (int y = 0; y < height; y += 3)
        {
            Assert.Equal(Vector3.UnitY, BlockRotation.TransformDirection(blocks[(4, y, 4)].RotationCode, -Vector3.UnitY));
            Assert.Equal(-Vector3.UnitY, BlockRotation.TransformDirection(blocks[(4, y + 2, 4)].RotationCode, -Vector3.UnitY));
            Assert.Equal(-Vector3.UnitZ, BlockRotation.TransformDirection(blocks[(4, y, 4)].RotationCode, Vector3.UnitZ));
            Assert.Equal(-Vector3.UnitZ, BlockRotation.TransformDirection(blocks[(4, y + 1, 4)].RotationCode, Vector3.UnitY));
            Assert.Equal(Vector3.UnitY, BlockRotation.TransformDirection(blocks[(4, y, 5)].RotationCode, Vector3.UnitZ));
            Assert.Equal(Vector3.UnitZ, BlockRotation.TransformDirection(blocks[(4, y, 5)].RotationCode, Vector3.UnitY));
            Assert.Equal(-Vector3.UnitY, BlockRotation.TransformDirection(blocks[(4, y + 2, 5)].RotationCode, Vector3.UnitZ));
            Assert.Equal(-Vector3.UnitZ, BlockRotation.TransformDirection(blocks[(4, y + 2, 5)].RotationCode, Vector3.UnitY));
        }
    }

    [Fact]
    public void NativeDeckIntakesKeepTheirRollAndCoolersReflectNorthSouth()
    {
        var cooler = new CoolerSnakeResult
        {
            Status = CoolerSnakeStatus.Sat,
            IntakeCells = [new(2, 2, 0, true), new(2, 3, 0, false)],
            CoolerCells = [new(3, 2, 0, 0, false, CoolerFaceFlags.North), new(3, 3, 0, 1, false, CoolerFaceFlags.South)]
        };
        var blocks = ApsBlockLayout.Build([new(4, 4, 0)], TemplateGenerator.Rectangle(9, 9), TetrisType.FourClip,
            new(2, ExportExtraLayers.EjectorsIntakesCoolerSnake, cooler), BlockOrientationConvention.NativePrefab);
        Assert.Equal(-Vector3.UnitZ, BlockRotation.TransformDirection(blocks[(2, -1, 6)].RotationCode, Vector3.UnitY));
        Assert.Equal(Vector3.UnitZ, BlockRotation.TransformDirection(blocks[(3, 2, 6)].RotationCode, Vector3.UnitY));
        Assert.Equal(CoolerBlockProfile.SelectBlock([Face.Forward]).Blr, blocks[(2, 2, 5)].RotationCode);
        Assert.Equal(CoolerBlockProfile.SelectBlock([Face.Back]).Blr, blocks[(3, 3, 5)].RotationCode);
    }

    [Theory]
    [InlineData(9, -4)]
    [InlineData(10, -4)]
    public void PreviewAndNativePlacementShareCenteringAndLayerHeights(int width, int minimumX)
    {
        var geometry = new ApsLayoutGeometry(width, 9, 2, TetrisType.FourClip, ExportExtraLayers.EjectorsIntakesCoolerSnake);
        Assert.Equal(minimumX, geometry.MinimumX);
        Assert.Equal(1, geometry.TetrisBottom);
        Assert.Equal(3, geometry.TetrisTop);
        Assert.Equal(4, geometry.IntendedHeight);
        var five = geometry with { Type = TetrisType.FiveClip, ComponentHeight = 6 };
        Assert.Equal(0, five.TetrisBottom);
        Assert.Equal(7, five.IntendedHeight);
        Assert.Equal(3, five.SectionHeight);
    }
}
