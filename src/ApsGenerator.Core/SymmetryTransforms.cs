using ApsGenerator.Core.Models;

namespace ApsGenerator.Core;

/// <summary>
/// Cell-coordinate symmetry transforms used by both the solver and UI.
/// </summary>
public static class SymmetryTransforms
{
    /// <summary>
    /// Returns all symmetric cell positions for a given (row, col) under the specified symmetry type.
    /// The original position is always included.
    /// </summary>
    public static IReadOnlyList<(int Row, int Col)> GetSymmetricPositions(
        int row, int col, int gridWidth, int gridHeight, SymmetryType symmetryType)
    {
        if (symmetryType == SymmetryType.Rotation90 && gridHeight != gridWidth)
            throw new ArgumentException("90° rotation symmetry requires a square grid.", nameof(symmetryType));

        var seen = new HashSet<(int Row, int Col)>();
        var result = new List<(int Row, int Col)>(4);

        void Add(int r, int c)
        {
            var position = (Row: r, Col: c);
            if (seen.Add(position))
                result.Add(position);
        }

        Add(row, col);

        switch (symmetryType)
        {
            case SymmetryType.None:
                break;

            case SymmetryType.HorizontalReflection:
                Add(gridHeight - 1 - row, col);
                break;

            case SymmetryType.VerticalReflection:
                Add(row, gridWidth - 1 - col);
                break;

            case SymmetryType.BothReflection:
                Add(gridHeight - 1 - row, col);
                Add(row, gridWidth - 1 - col);
                Add(gridHeight - 1 - row, gridWidth - 1 - col);
                break;

            case SymmetryType.Rotation180:
                Add(gridHeight - 1 - row, gridWidth - 1 - col);
                break;

            case SymmetryType.Rotation90:
                Add(col, gridHeight - 1 - row);
                Add(gridHeight - 1 - col, row);
                Add(gridHeight - 1 - row, gridWidth - 1 - col);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(symmetryType), symmetryType, "Unsupported symmetry type.");
        }

        return result;
    }
}