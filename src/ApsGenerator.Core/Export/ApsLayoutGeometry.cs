using ApsGenerator.Core.Models;

namespace ApsGenerator.Core.Export;

public readonly record struct ApsLayoutGeometry(int Width, int Depth, int ComponentHeight, TetrisType Type, ExportExtraLayers Layers)
{
    public const int FiveClipSectionHeight = 3;
    public int MinimumX => -(Width / 2) + (Width % 2 == 0 ? 1 : 0);
    public bool HasBottomLayer => Type != TetrisType.FiveClip && Layers != ExportExtraLayers.TetrisOnly;
    public bool HasCoolerLayer => Layers.NeedsCoolerSolve(Type);
    public int TetrisBottom => HasBottomLayer ? 1 : 0;
    public int TetrisTop => TetrisBottom + ComponentHeight;
    public int IntendedHeight => TetrisTop + (HasCoolerLayer ? 1 : 0);
    public int SectionHeight => Type == TetrisType.FiveClip ? FiveClipSectionHeight : ComponentHeight;

    public (int X, int Z) Position(int row, int column) => (column, Depth - 1 - row);
}
