using ApsGenerator.Core.Models;

namespace ApsGenerator.Core;

/// <summary>Shared placement footprint coverage helpers.</summary>
public static class PlacementCoverage
{
    /// <summary>
    /// Available cells minus exclusive (non-connection) cells covered by placements.
    /// Connection cells are omitted so shared 5-clip links are not double-counted.
    /// </summary>
    public static int EmptyExclusiveCellCount(
        IEnumerable<Placement> placements,
        IReadOnlyList<ClusterShape> shapes,
        int availableCellCount)
    {
        var covered = new HashSet<(int, int)>();
        foreach (var placement in placements)
        {
            var offsets = shapes[placement.ShapeIndex].Offsets;
            for (int j = 0; j < offsets.Count; j++)
            {
                if (offsets[j].Role == CellRole.Connection)
                    continue;

                covered.Add((
                    placement.Row + offsets[j].DeltaRow,
                    placement.Col + offsets[j].DeltaCol));
            }
        }

        return availableCellCount - covered.Count;
    }
}
