namespace ApsGenerator.Core.Models;

public static class TetrisTypeExtensions
{
    public static int ClipCount(this TetrisType type) => type switch
    {
        TetrisType.ThreeClip => 3,
        TetrisType.FourClip => 4,
        TetrisType.FiveClip => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported tetris type.")
    };

    public static int EffectiveAutoloadersPerPlacement(this TetrisType type) =>
        type.ClipCount() + 1;

    public static bool SupportsCoolerSnakes(this TetrisType type) =>
        type is TetrisType.ThreeClip or TetrisType.FourClip or TetrisType.FiveClip;
}
