using ApsGenerator.Core.Models;

namespace ApsGenerator.Core;

public static class PlacementEnumerator
{
    public static List<Placement> Enumerate(Grid grid, TetrisType type)
    {
        var shapes = ClusterShape.GetShapes(type);
        var placements = new List<Placement>();

        for (int shapeIndex = 0; shapeIndex < shapes.Count; shapeIndex++)
        {
            var offsets = shapes[shapeIndex].Offsets;

            for (int r = 0; r < grid.Height; r++)
            for (int c = 0; c < grid.Width; c++)
            {
                if (IsValidPlacement(grid, offsets, r, c))
                    placements.Add(new Placement(r, c, shapeIndex));
            }
        }

        return placements;
    }

    private static bool IsValidPlacement(Grid grid, IReadOnlyList<CellOffset> offsets, int row, int col)
    {
        for (int i = 0; i < offsets.Count; i++)
        {
            var offset = offsets[i];
            if (!grid.IsAvailable(row + offset.DeltaRow, col + offset.DeltaCol))
                return false;
        }

        return true;
    }
}
