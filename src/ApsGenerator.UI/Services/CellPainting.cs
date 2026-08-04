using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.UI.Models;

namespace ApsGenerator.UI.Services;

internal static class CellPainting
{
    public static CellState NextState(CellState current, PaintMode mode) => mode switch
    {
        PaintMode.Toggle => current == CellState.Available ? CellState.Blocked : CellState.Available,
        PaintMode.Clear => CellState.Available,
        _ => CellState.Blocked,
    };

    public static IReadOnlyList<(int Row, int Col)> PositionsToPaint(
        int row,
        int col,
        int width,
        int height,
        SymmetryType symmetryType)
    {
        if (symmetryType == SymmetryType.None)
            return [(row, col)];

        if (symmetryType == SymmetryType.Rotation90 && width != height)
            return [(row, col)];

        return SymmetryTransforms.GetSymmetricPositions(row, col, width, height, symmetryType);
    }
}
