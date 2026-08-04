using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

/// <summary>
/// Partition Available cells separated by Blocked walls into independent cooler-snake sectors.
/// </summary>
internal static class CoolerSectors
{
    public sealed record SectorSlice(
        PackingAnalysis Packing,
        int[] LocalToGlobal,
        int SnakeId);

    /// <summary>Flood-fill Available cells into 4-neighbor components.</summary>
    public static List<HashSet<CellKey>> AvailableComponents(Grid grid)
    {
        var seen = new bool[grid.Height, grid.Width];
        var components = new List<HashSet<CellKey>>();

        for (int r = 0; r < grid.Height; r++)
        {
            for (int c = 0; c < grid.Width; c++)
            {
                if (!grid.IsAvailable(r, c) || seen[r, c])
                    continue;

                var component = FloodFill(grid, r, c, seen);
                components.Add(component);
            }
        }

        return components;
    }

    /// <summary>
    /// Occupied Available components as sliced packings (local cluster indices 0..k-1).
    /// One occupied region yields one slice.
    /// </summary>
    public static IReadOnlyList<SectorSlice> PartitionOccupied(Grid grid, PackingAnalysis packing)
    {
        var components = AvailableComponents(grid);
        var occupied = new List<(HashSet<CellKey> Cells, List<ClusterInfo> Clusters)>();

        foreach (var component in components)
        {
            var clusters = packing.Clusters
                .Where(c => component.Contains(c.Loader))
                .OrderBy(c => c.Index)
                .ToList();
            if (clusters.Count == 0)
                continue;

            occupied.Add((component, clusters));
        }

        var slices = new List<SectorSlice>(occupied.Count);
        for (int snakeId = 0; snakeId < occupied.Count; snakeId++)
        {
            var (cells, clusters) = occupied[snakeId];
            slices.Add(Slice(packing.TetrisType, cells, clusters, snakeId));
        }

        return slices;
    }

    public static CoolerSnakeResult Merge(
        IReadOnlyList<(CoolerSnakeResult Result, int[] LocalToGlobal, int SnakeId)> parts,
        int globalClusterCount,
        TetrisType type)
    {
        var coolers = new List<CoolerCell>();
        var intakes = new List<IntakeCell>();
        var ejectors = new List<EjectorPlacement>();
        var intakesPerCluster = new int[globalClusterCount];
        int layers = 1;
        double seconds = 0;
        var details = new List<string>(parts.Count);

        foreach (var (result, localToGlobal, snakeId) in parts)
        {
            foreach (var cell in result.CoolerCells)
                coolers.Add(cell with { SnakeId = snakeId });

            intakes.AddRange(result.IntakeCells);

            foreach (var ejector in result.EjectorDirs)
                ejectors.Add(ejector with { ClusterIndex = localToGlobal[ejector.ClusterIndex] });

            for (int local = 0; local < result.IntakesPerCluster.Count; local++)
                intakesPerCluster[localToGlobal[local]] = result.IntakesPerCluster[local];

            layers = Math.Max(layers, result.LayersUsed);
            seconds += result.SolveSeconds;
            details.Add(result.Detail);
        }

        string detail = parts.Count == 1
            ? details[0]
            : $"{parts.Count} sectors: {string.Join("; ", details)}";

        return new CoolerSnakeResult
        {
            Status = CoolerSnakeStatus.Sat,
            LayersUsed = layers,
            CoolerCells = coolers,
            IntakeCells = intakes,
            EjectorDirs = ejectors,
            RequiredIntakesPerCluster = EjectorCatalog.RequiredTotalIntakes(type),
            IntakesPerCluster = intakesPerCluster,
            SolveSeconds = seconds,
            Detail = detail,
        };
    }

    /// <summary>Stamp SnakeId onto cooler cells from independent sector solves.</summary>
    public static List<CoolerCell> WithSnakeIds(
        IReadOnlyList<(IReadOnlyList<CoolerCell> Cells, int SnakeId)> parts)
    {
        var coolers = new List<CoolerCell>();
        foreach (var (cells, snakeId) in parts)
        {
            foreach (var cell in cells)
                coolers.Add(cell with { SnakeId = snakeId });
        }

        return coolers;
    }

    private static SectorSlice Slice(
        TetrisType type,
        HashSet<CellKey> sectorCells,
        List<ClusterInfo> clusters,
        int snakeId)
    {
        var localToGlobal = new int[clusters.Count];
        var renumbered = new List<ClusterInfo>(clusters.Count);
        for (int i = 0; i < clusters.Count; i++)
        {
            var c = clusters[i];
            localToGlobal[i] = c.Index;
            renumbered.Add(new ClusterInfo
            {
                Index = i,
                Placement = c.Placement,
                Loader = c.Loader,
                Clips = c.Clips,
                Footprint = c.Footprint,
                OpenArmDelta = c.OpenArmDelta,
            });
        }

        var packing = PackingAnalyzer.FromHandcrafted(type, renumbered);
        var routing = sectorCells
            .OrderBy(c => c.Row)
            .ThenBy(c => c.Col)
            .ToList();

        return new SectorSlice(packing.WithRoutingCells(routing), localToGlobal, snakeId);
    }

    private static HashSet<CellKey> FloodFill(Grid grid, int startRow, int startCol, bool[,] seen)
    {
        var component = new HashSet<CellKey>();
        var queue = new Queue<CellKey>();
        var start = new CellKey(startRow, startCol);
        seen[startRow, startCol] = true;
        queue.Enqueue(start);
        component.Add(start);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var nb in cur.CardinalNeighbors())
            {
                if (!grid.IsAvailable(nb.Row, nb.Col) || seen[nb.Row, nb.Col])
                    continue;

                seen[nb.Row, nb.Col] = true;
                component.Add(nb);
                queue.Enqueue(nb);
            }
        }

        return component;
    }
}
