using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

internal readonly record struct CellKey(int Row, int Col)
{
    public IEnumerable<CellKey> CardinalNeighbors()
    {
        int row = Row;
        int col = Col;
        return CoolerCardinals.Offsets.Select(o => new CellKey(row + o.Dr, col + o.Dc));
    }
}

internal sealed class ClusterInfo
{
    public required int Index { get; init; }
    public Placement? Placement { get; init; }
    public required CellKey Loader { get; init; }
    public required IReadOnlyList<CellKey> Clips { get; init; }
    public required IReadOnlyList<CellKey> Footprint { get; init; }

    /// <summary>Open T-arm for 3-clip (delta from loader). Null for 4-clip.</summary>
    public (int DRow, int DCol)? OpenArmDelta { get; init; }

    public CellKey? OpenArmCell =>
        OpenArmDelta is { } d
            ? new CellKey(Loader.Row + d.DRow, Loader.Col + d.DCol)
            : null;
}

internal sealed class PackingAnalysis
{
    public required TetrisType TetrisType { get; init; }
    public required IReadOnlyList<ClusterInfo> Clusters { get; init; }
    /// <summary>Cooler routing domain (exclusive footprint, optionally ∪ empties).</summary>
    public required IReadOnlyList<CellKey> FootprintCells { get; init; }
    public required IReadOnlyList<CellKey> ExclusiveCells { get; init; }
    public required IReadOnlyList<CellKey> LoaderCells { get; init; }
    public required IReadOnlyList<IReadOnlyList<CellKey>> LoaderChains { get; init; }

    public PackingAnalysis WithRoutingCells(IReadOnlyList<CellKey> routing) => new()
    {
        TetrisType = TetrisType,
        Clusters = Clusters,
        FootprintCells = routing,
        ExclusiveCells = ExclusiveCells,
        LoaderCells = LoaderCells,
        LoaderChains = LoaderChains,
    };
}

internal static class PackingAnalyzer
{
    public static PackingAnalysis Analyze(
        Grid grid,
        TetrisType type,
        IReadOnlyList<Placement> placements)
    {
        var shapes = ClusterShape.GetShapes(type);
        var clusters = new List<ClusterInfo>(placements.Count);
        var footprintSet = new HashSet<CellKey>();
        var loaders = new List<CellKey>(placements.Count);

        for (int i = 0; i < placements.Count; i++)
        {
            var p = placements[i];
            var shape = shapes[p.ShapeIndex];
            CellKey? loader = null;
            var clips = new List<CellKey>();
            var footprint = new List<CellKey>();

            foreach (var offset in shape.Offsets)
            {
                if (offset.Role == CellRole.Connection)
                    continue;

                var cell = new CellKey(p.Row + offset.DeltaRow, p.Col + offset.DeltaCol);
                footprint.Add(cell);
                footprintSet.Add(cell);

                if (offset.Role == CellRole.Loader)
                {
                    loader = cell;
                    loaders.Add(cell);
                }
                else if (offset.Role == CellRole.Clip)
                {
                    clips.Add(cell);
                }
            }

            if (loader is null)
                throw new InvalidOperationException($"Cluster {i} has no loader cell.");

            clusters.Add(new ClusterInfo
            {
                Index = i,
                Placement = p,
                Loader = loader.Value,
                Clips = clips,
                Footprint = footprint,
                OpenArmDelta = type == TetrisType.ThreeClip
                    ? ClusterOpenArm.Delta(shape)
                    : null,
            });
        }

        return FromClusters(type, clusters, loaders, footprintSet);
    }

    public static PackingAnalysis FromHandcrafted(
        TetrisType type,
        IReadOnlyList<ClusterInfo> clusters)
    {
        var footprintSet = clusters.SelectMany(c => c.Footprint).ToHashSet();
        var loaders = clusters.Select(c => c.Loader).ToList();
        return FromClusters(type, [.. clusters], loaders, footprintSet);
    }

    private static PackingAnalysis FromClusters(
        TetrisType type,
        List<ClusterInfo> clusters,
        List<CellKey> loaders,
        HashSet<CellKey> footprintSet)
    {
        var exclusive = footprintSet.OrderBy(c => c.Row).ThenBy(c => c.Col).ToList();
        var loaderSet = loaders.ToHashSet();
        var chains = BuildLoaderChains(loaders, loaderSet);

        return new PackingAnalysis
        {
            TetrisType = type,
            Clusters = clusters,
            FootprintCells = exclusive,
            ExclusiveCells = exclusive,
            LoaderCells = loaders,
            LoaderChains = chains,
        };
    }

    private static List<IReadOnlyList<CellKey>> BuildLoaderChains(
        IReadOnlyList<CellKey> loaders,
        HashSet<CellKey> loaderSet)
    {
        var remaining = new HashSet<CellKey>(loaders);
        var chains = new List<IReadOnlyList<CellKey>>();

        while (remaining.Count > 0)
        {
            var start = remaining.First();
            remaining.Remove(start);
            var chain = new List<CellKey> { start };
            var queue = new Queue<CellKey>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var n in cur.CardinalNeighbors())
                {
                    if (!loaderSet.Contains(n) || !remaining.Remove(n))
                        continue;
                    chain.Add(n);
                    queue.Enqueue(n);
                }
            }

            chains.Add(chain);
        }

        return chains;
    }
}
