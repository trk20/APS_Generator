namespace ApsGenerator.Core.Models;

/// <summary>Open cooler faces on the deck (N, E, S, W).</summary>
[Flags]
public enum CoolerFaceFlags : byte
{
    None = 0,
    North = 1,
    East = 2,
    South = 4,
    West = 8,
}

public static class CoolerFaceFlagsExtensions
{
    public static bool Has(this CoolerFaceFlags flags, int dir) =>
        (flags & CoolerCardinals.FlagFor(dir)) != 0;

    public static CoolerFaceFlags With(this CoolerFaceFlags flags, int dir) =>
        flags | CoolerCardinals.FlagFor(dir);
}
