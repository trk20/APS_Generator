using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

/// <summary>
/// Helpers for extending cooler routing domains with empty / bypass cells.
/// </summary>
internal static class DomainExtend
{
    public static PackingAnalysis AddBypassCell(PackingAnalysis packing, CellKey bypass)
    {
        var set = packing.FootprintCells.ToHashSet();
        set.Add(bypass);
        return packing.WithRoutingCells(set.OrderBy(x => x.Row).ThenBy(x => x.Col).ToList());
    }

    public static HashSet<CellKey> EmptyAvailable(PackingAnalysis packing, Grid grid)
    {
        var exclusive = packing.ExclusiveCells.ToHashSet();
        return EnumerateAvailable(grid).Where(k => !exclusive.Contains(k)).ToHashSet();
    }

    private static IEnumerable<CellKey> EnumerateAvailable(Grid grid)
    {
        for (int r = 0; r < grid.Height; r++)
        {
            for (int c = 0; c < grid.Width; c++)
            {
                if (grid.IsAvailable(r, c))
                    yield return new CellKey(r, c);
            }
        }
    }
}
