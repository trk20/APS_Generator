using System.Numerics;
using ApsGenerator.Core.Models;

namespace ApsGenerator.Core.Export;

public enum BlockOrientationConvention { BlueprintFile, NativePrefab }

internal sealed class BlockOrientation(BlockOrientationConvention convention)
{
    private Vector3 Direction(Vector3 direction) => convention == BlockOrientationConvention.NativePrefab
        ? new Vector3(direction.X, direction.Y, -direction.Z)
        : direction;

    internal int Loader(Vector3 direction) => BlockRotation.FindRotation(
        Vector3.UnitY, Direction(direction), Vector3.UnitZ, Vector3.UnitY);

    internal int Clip(Vector3 direction) => BlockRotation.FindRotation(
        -Vector3.UnitY, Direction(direction), Vector3.UnitZ, Vector3.UnitY);

    internal int VerticalClip(Vector3 direction) => BlockRotation.FindRotation(
        -Vector3.UnitY, direction, Vector3.UnitZ, Direction(Vector3.UnitZ));

    internal int Intake(Vector3 facing, bool stacked = false)
    {
        Vector3 secondary = facing.Y > 0 ? -Vector3.UnitZ : Vector3.UnitZ;
        return BlockRotation.FindRotation(Vector3.UnitZ, facing, Vector3.UnitY,
            stacked ? Direction(secondary) : secondary);
    }

    internal int Ejector(Vector3 facing, Vector3 secondary) => BlockRotation.FindRotation(
        Vector3.UnitZ, Direction(facing), Vector3.UnitY, Direction(secondary));

    internal (int BlockId, int Rotation) Cooler(CoolerCell cell)
    {
        var faces = CoolerBlockProfile.FacesFrom(cell.OpenFaces, cell.ConnectUp, cell.ConnectDown);
        var oriented = faces.Select(face => BlockRotation.VectorToFace(Direction(BlockRotation.FaceToVector(face)))).ToArray();
        return CoolerBlockProfile.SelectBlock(oriented);
    }
}
