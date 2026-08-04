namespace ApsGenerator.Core.Models;

/// <summary>3-clip missing plus-arm relative to the loader (grid row/col delta).</summary>
public static class ClusterOpenArm
{
    public static (int DRow, int DCol) Delta(ClusterShape shape)
    {
        var loader = shape.Offsets.First(o => o.Role == CellRole.Loader);
        var occupied = shape.Offsets
            .Where(o => o.Role == CellRole.Clip)
            .Select(o => (o.DeltaRow, o.DeltaCol))
            .ToHashSet();

        var (dr, dc) = CoolerCardinals.Offsets.First(off =>
            !occupied.Contains((loader.DeltaRow + off.Dr, loader.DeltaCol + off.Dc)));
        return (dr, dc);
    }
}
