using ApsGenerator.Core.Models;
using ApsGenerator.UI.Models;

namespace ApsGenerator.UI.ViewModels;

public static class EnumValues
{
    public static EnumDisplayItem<TetrisType>[] TetrisTypes { get; } =
    [
        new(TetrisType.ThreeClip, "3-Clip"),
        new(TetrisType.FourClip, "4-Clip"),
        new(TetrisType.FiveClip, "5-Clip")
    ];

    public static EnumDisplayItem<SymmetryType>[] SymmetryTypes { get; } =
    [
        new(SymmetryType.None, "None"),
        new(SymmetryType.HorizontalReflection, "Reflexive - Horizontal"),
        new(SymmetryType.VerticalReflection, "Reflexive - Vertical"),
        new(SymmetryType.BothReflection, "Reflexive - Both"),
        new(SymmetryType.Rotation180, "Rotational - 180°"),
        new(SymmetryType.Rotation90, "Rotational - 90°")
    ];
}
