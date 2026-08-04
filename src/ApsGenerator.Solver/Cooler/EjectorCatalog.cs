using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

/// <summary>One legal ejector placement for a cluster, with derived top-intake deficit.</summary>
internal sealed class EjectorCandidate
{
    public required int ClusterIndex { get; init; }
    public required EjectorKind Kind { get; init; }
    public required CellKey Loader { get; init; }
    public required CellKey Protrusion { get; init; }
    public required int TopDeficit { get; init; }
    public required int Quota { get; init; }

    /// <summary>Footprint cells that keep a free bottom intake under this choice.</summary>
    public required IReadOnlyList<CellKey> BottomIntakeCells { get; init; }

    public EjectorPlacement ToPlacement() => new(
        ClusterIndex,
        Kind,
        Loader.Row,
        Loader.Col,
        Protrusion.Row,
        Protrusion.Col,
        TopDeficit);
}

/// <summary>
/// Legal ejector candidates per cluster.
/// Bottom into own clips; 3-clip vertical open-arm down when free.
/// </summary>
internal static class EjectorCatalog
{

    public static int RequiredTotalIntakes(TetrisType type) => type switch
    {
        TetrisType.ThreeClip => 4,
        TetrisType.FourClip => 5,
        _ => 0,
    };

    public static IReadOnlyList<IReadOnlyList<EjectorCandidate>> Build(
        PackingAnalysis packing,
        Grid? grid)
    {
        int quota = RequiredTotalIntakes(packing.TetrisType);
        HashSet<CellKey>? empties = grid is null ? null : DomainExtend.EmptyAvailable(packing, grid);
        var exclusive = packing.ExclusiveCells.ToHashSet();
        var perCluster = new List<IReadOnlyList<EjectorCandidate>>(packing.Clusters.Count);

        foreach (var cluster in packing.Clusters.OrderBy(c => c.Index))
        {
            var list = new List<EjectorCandidate>();
            AddBottomCandidates(list, cluster, quota);

            if (packing.TetrisType == TetrisType.ThreeClip)
                TryAddVerticalOpenArmDown(list, cluster, quota, packing, grid, empties, exclusive);

            if (list.Count == 0)
                throw new InvalidOperationException($"Cluster {cluster.Index} has no legal ejector candidates.");

            perCluster.Add(list);
        }

        return perCluster;
    }

    /// <summary>
    /// One intake-only candidate per cluster: bottoms under loader+clips, typically zero top deficit.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<EjectorCandidate>> BuildIntakeOnly(PackingAnalysis packing)
    {
        int quota = RequiredTotalIntakes(packing.TetrisType);
        return [.. packing.Clusters
            .OrderBy(c => c.Index)
            .Select(cluster => (IReadOnlyList<EjectorCandidate>)
            [
                MakeCandidate(
                    cluster,
                    EjectorKind.None,
                    cluster.Loader,
                    cluster.Clips.Append(cluster.Loader).ToList(),
                    quota),
            ])];
    }

    private static EjectorCandidate MakeCandidate(
        ClusterInfo cluster,
        EjectorKind kind,
        CellKey protrusion,
        IReadOnlyList<CellKey> bottomCells,
        int quota) =>
        new()
        {
            ClusterIndex = cluster.Index,
            Kind = kind,
            Loader = cluster.Loader,
            Protrusion = protrusion,
            TopDeficit = Math.Max(0, quota - bottomCells.Count),
            Quota = quota,
            BottomIntakeCells = bottomCells,
        };

    private static void AddBottomCandidates(List<EjectorCandidate> list, ClusterInfo cluster, int quota) =>
        list.AddRange(
            cluster.Clips
                .Where(clip =>
                    Math.Abs(clip.Row - cluster.Loader.Row) + Math.Abs(clip.Col - cluster.Loader.Col) == 1)
                .Select(clip => MakeCandidate(
                    cluster,
                    EjectorKind.Bottom,
                    clip,
                    cluster.Clips.Where(c => !c.Equals(clip)).ToList(),
                    quota)));

    private static void TryAddVerticalOpenArmDown(
        List<EjectorCandidate> list,
        ClusterInfo cluster,
        int quota,
        PackingAnalysis packing,
        Grid? grid,
        HashSet<CellKey>? empties,
        HashSet<CellKey> exclusive)
    {
        if (cluster.OpenArmCell is not { } arm)
            return;

        if (!IsEmptyNeighborFree(arm, packing, grid, empties, exclusive))
            return;

        list.Add(MakeCandidate(
            cluster,
            EjectorKind.VerticalOpenArmDown,
            arm,
            cluster.Clips.Append(cluster.Loader).ToList(),
            quota));
    }

    public static bool IsEmptyNeighborFree(
        CellKey cell,
        PackingAnalysis packing,
        Grid? grid,
        HashSet<CellKey>? empties,
        HashSet<CellKey> exclusive)
    {
        if (exclusive.Contains(cell))
            return false;

        if (packing.Clusters.Any(o =>
                o.Loader.Equals(cell) || o.Clips.Contains(cell) || o.Footprint.Contains(cell)))
            return false;

        if (grid is not null && !grid.IsInBounds(cell.Row, cell.Col))
            return false;

        if (grid is not null && !grid.IsAvailable(cell.Row, cell.Col))
            return false;

        if (empties is not null && !empties.Contains(cell))
            return false;

        return true;
    }
}
