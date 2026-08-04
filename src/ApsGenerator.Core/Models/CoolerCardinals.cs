namespace ApsGenerator.Core.Models;

/// <summary>Single source of truth for cooler cardinal directions (N, E, S, W).</summary>
public static class CoolerCardinals
{
    /// <summary>N, E, S, W row/col offsets.</summary>
    public static readonly (int Dr, int Dc)[] Offsets = [(-1, 0), (0, 1), (1, 0), (0, -1)];

    /// <summary>Opposite direction index for each cardinal.</summary>
    public static readonly int[] Opposite = [2, 3, 0, 1];

    public static CoolerFaceFlags FlagFor(int dir) => dir switch
    {
        0 => CoolerFaceFlags.North,
        1 => CoolerFaceFlags.East,
        2 => CoolerFaceFlags.South,
        3 => CoolerFaceFlags.West,
        _ => CoolerFaceFlags.None,
    };

    public static int OppositeOf(int dir) =>
        (uint)dir < 4 ? Opposite[dir] : dir;

    public static (int Dr, int Dc) Offset(int dir) =>
        (uint)dir < 4 ? Offsets[dir] : (0, 0);
}
