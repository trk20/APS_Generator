using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

/// <summary>
/// Shared top-intake placement for constructive / local-bridge paths.
/// Matches undirected/SAT policy: do not put intakes on loaders when clips can cover K,
/// and never top both loaders of an adjacent pair.
/// Prefers leaf clips (few external neighbors) so deck corridors stay open for routing.
/// </summary>
internal static class CoolerIntakeAssign
{
    public static bool TryAssign(
        CoolerRoutingContext ctx,
        Dictionary<int, EjectorCandidate> byCluster,
        bool[] intakeMask,
        Random? random)
    {
        Array.Clear(intakeMask);
        var packing = ctx.Packing;
        var exclusiveSet = ctx.ExclusiveSet;
        var loaderIndex = packing.LoaderCells
            .Where(exclusiveSet.Contains)
            .ToDictionary(c => c, c => ctx.IndexOf[c]);

        var footprintSet = packing.FootprintCells.ToHashSet();
        var clusterOf = BuildClusterOf(packing);

        foreach (var cluster in packing.Clusters)
        {
            int k = byCluster[cluster.Index].TopDeficit;
            if (k <= 0)
                continue;

            if (!PlaceForCluster(
                    cluster, k, ctx, exclusiveSet, loaderIndex, footprintSet, clusterOf,
                    intakeMask, random))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Yields deterministic then randomized intake masks for constructive / bridge search.
    /// </summary>
    public static IEnumerable<bool[]> EnumerateMaskTrials(
        CoolerRoutingContext ctx,
        IReadOnlyList<EjectorCandidate> assignment,
        int randomTrials,
        int randomSeed,
        bool tryMinimizingComponents = false)
    {
        var byCluster = assignment.ToDictionary(a => a.ClusterIndex);
        var mask = new bool[ctx.CellCount];

        if (tryMinimizingComponents
            && TryAssignMinimizingComponents(ctx, byCluster, mask))
            yield return (bool[])mask.Clone();

        if (TryAssign(ctx, byCluster, mask, random: null))
            yield return (bool[])mask.Clone();

        var rng = new Random(randomSeed);
        for (int t = 0; t < randomTrials; t++)
        {
            Array.Clear(mask);
            if (TryAssign(ctx, byCluster, mask, rng))
                yield return (bool[])mask.Clone();
        }
    }

    private static Dictionary<CellKey, int> BuildClusterOf(PackingAnalysis packing) =>
        packing.Clusters
            .SelectMany(c => c.Footprint.Select(cell => (cell, c.Index)))
            .ToDictionary(x => x.cell, x => x.Index);

    private static bool PlaceForCluster(
        ClusterInfo cluster,
        int k,
        CoolerRoutingContext ctx,
        HashSet<CellKey> exclusiveSet,
        Dictionary<CellKey, int> loaderIndex,
        HashSet<CellKey> footprintSet,
        Dictionary<CellKey, int> clusterOf,
        bool[] intakeMask,
        Random? random)
    {
        int loaderIdx = ctx.IndexOf[cluster.Loader];
        var nonLoaderCells = cluster.Footprint
            .Where(exclusiveSet.Contains)
            .Where(c => !c.Equals(cluster.Loader))
            .ToList();

        // Allow loader cell as a top intake when non-loader exclusive cells cannot cover K
        bool allowLoader = nonLoaderCells.Count < k;
        var candidates = RankCandidates(
            nonLoaderCells, cluster, ctx, footprintSet, clusterOf, loaderIndex, random);

        if (allowLoader)
            candidates.Add(loaderIdx);

        int placed = 0;
        foreach (int i in candidates)
        {
            if (intakeMask[i])
                continue;

            if (i == loaderIdx && cluster.Loader.CardinalNeighbors().Any(nb =>
                loaderIndex.TryGetValue(nb, out int nbIdx) && intakeMask[nbIdx]))
                continue;

            intakeMask[i] = true;
            placed++;
            if (placed >= k)
                break;
        }

        return placed >= k;
    }

    private static List<int> RankCandidates(
        List<CellKey> nonLoaderCells,
        ClusterInfo cluster,
        CoolerRoutingContext ctx,
        HashSet<CellKey> footprintSet,
        Dictionary<CellKey, int> clusterOf,
        Dictionary<CellKey, int> loaderIndex,
        Random? random)
    {
        var indexOf = ctx.IndexOf;

        var ranked = nonLoaderCells
            .Select(c => (
                Idx: indexOf[c],
                External: CountExternalNeighbors(c, cluster.Index, footprintSet, clusterOf),
                TouchOtherLoader: TouchesOtherLoader(c, cluster.Loader, loaderIndex),
                Degree: ctx.Neighbors[indexOf[c]].Count))
            .OrderBy(x => x.External)
            .ThenBy(x => x.TouchOtherLoader ? 1 : 0)
            .ThenBy(x => x.Degree);

        if (random is null)
            return [.. ranked.Select(x => x.Idx)];

        return [.. ranked
            .Select(x => (x.Idx, x.External, x.Degree, jitter: random.Next(3)))
            .OrderBy(x => x.External)
            .ThenBy(x => x.Degree)
            .ThenBy(x => x.jitter)
            .Select(x => x.Idx)];
    }

    private static int CountExternalNeighbors(
        CellKey cell,
        int clusterIndex,
        HashSet<CellKey> footprintSet,
        Dictionary<CellKey, int> clusterOf) =>
        cell.CardinalNeighbors().Count(nb =>
            footprintSet.Contains(nb)
            && clusterOf.TryGetValue(nb, out int other)
            && other != clusterIndex);

    private static bool TouchesOtherLoader(
        CellKey cell,
        CellKey ownLoader,
        Dictionary<CellKey, int> loaderIndex) =>
        cell.CardinalNeighbors().Any(nb => !nb.Equals(ownLoader) && loaderIndex.ContainsKey(nb));

    /// <summary>
    /// Place intakes preferring layouts that keep loader-chain terminals in few deck components
    /// (so local bridges stay short). Falls back to leaf-preferring assign.
    /// </summary>
    public static bool TryAssignMinimizingComponents(
        CoolerRoutingContext ctx,
        Dictionary<int, EjectorCandidate> byCluster,
        bool[] intakeMask)
    {
        Array.Clear(intakeMask);
        var packing = ctx.Packing;
        var exclusiveSet = ctx.ExclusiveSet;

        foreach (var cluster in packing.Clusters.OrderBy(c => c.Index))
        {
            int k = byCluster[cluster.Index].TopDeficit;
            if (k <= 0)
                continue;

            var nonLoader = cluster.Footprint
                .Where(exclusiveSet.Contains)
                .Where(c => !c.Equals(cluster.Loader))
                .Select(c => ctx.IndexOf[c])
                .Where(i => !intakeMask[i])
                .ToList();

            if (nonLoader.Count < k)
                return false;

            List<int>? bestCombo = null;
            int bestComponents = int.MaxValue;
            int bestCorridor = int.MaxValue;

            foreach (var combo in Combinations(nonLoader, k))
            {
                foreach (int i in combo)
                    intakeMask[i] = true;

                int components = CountTerminalComponents(ctx, intakeMask);

                // Prefer putting intakes on leaf clips when component count ties.
                int corridor = Enumerable.Range(0, intakeMask.Length)
                    .Where(i => intakeMask[i])
                    .Select(i => ctx.Neighbors[i].Count(nb => !intakeMask[nb]))
                    .Where(deckNeighbors => deckNeighbors >= 2)
                    .Sum();

                foreach (int i in combo)
                    intakeMask[i] = false;

                if (components > bestComponents)
                    continue;
                if (components == bestComponents && corridor >= bestCorridor)
                    continue;

                bestComponents = components;
                bestCorridor = corridor;
                bestCombo = combo;
            }

            if (bestCombo is null)
                return false;

            foreach (int i in bestCombo)
                intakeMask[i] = true;
        }

        return true;
    }

    private static IEnumerable<List<int>> Combinations(IReadOnlyList<int> items, int k)
    {
        if (k <= 0 || k > items.Count)
            yield break;

        var buf = new int[k];
        foreach (var _ in CombinationsRec(items, k, 0, 0, buf))
            yield return buf.ToList();
    }

    private static IEnumerable<int> CombinationsRec(
        IReadOnlyList<int> items,
        int k,
        int start,
        int depth,
        int[] buf)
    {
        if (depth == k)
        {
            yield return 0;
            yield break;
        }

        for (int i = start; i <= items.Count - (k - depth); i++)
        {
            buf[depth] = items[i];
            foreach (var ignored in CombinationsRec(items, k, i + 1, depth + 1, buf))
                yield return ignored;
        }
    }

    /// <summary>How many terminal-component fragments the intake mask creates on the deck.</summary>
    public static int CountTerminalComponents(CoolerRoutingContext ctx, bool[] intakeMask)
    {
        var active = ctx.ActiveWithoutIntakes(intakeMask);
        var terminals = CoolerGraph.PickChainTerminals(active, ctx.ChainLoaderIdx, ctx.Neighbors);
        if (terminals is null)
            return int.MaxValue;

        var componentOf = CoolerGraph.LabelComponents(active, ctx.Neighbors);
        return terminals.Select(t => componentOf[t]).Distinct().Count();
    }
}
