using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

/// <summary>Shared cardinal graph helpers for cooler routing.</summary>
internal static class CoolerGraph
{
    public static List<(int Nb, int Dir)>[] BuildNeighborsDir(
        IReadOnlyList<CellKey> cells,
        Dictionary<CellKey, int> indexOf)
    {
        int n = cells.Count;
        var neighbors = new List<(int, int)>[n];
        for (int i = 0; i < n; i++)
            neighbors[i] = [];

        for (int i = 0; i < n; i++)
        {
            var c = cells[i];
            for (int d = 0; d < 4; d++)
            {
                var (dr, dc) = CoolerCardinals.Offsets[d];
                var nb = new CellKey(c.Row + dr, c.Col + dc);
                if (!indexOf.TryGetValue(nb, out int j))
                    continue;
                neighbors[i].Add((j, d));
            }
        }

        return neighbors;
    }

    public static List<int>[] BuildNeighbors(
        IReadOnlyList<CellKey> cells,
        Dictionary<CellKey, int> indexOf) =>
        UndirectedFromDir(BuildNeighborsDir(cells, indexOf));

    public static List<int>[] UndirectedFromDir(List<(int Nb, int Dir)>[] neighborsDir)
    {
        int n = neighborsDir.Length;
        var neighbors = new List<int>[n];
        for (int i = 0; i < n; i++)
            neighbors[i] = neighborsDir[i].Select(t => t.Nb).ToList();

        return neighbors;
    }

    /// <summary>
    /// Unfiltered attachment set for a loader chain: loaders plus all neighbor cells.
    /// </summary>
    public static HashSet<int> AttachmentSet(
        IReadOnlyList<int> chainLoaderIdx,
        List<int>[] neighbors) =>
        chainLoaderIdx
            .SelectMany(loader => neighbors[loader].Prepend(loader))
            .ToHashSet();

    /// <summary>
    /// Attachment terminals for a loader chain on the active (non-intake) set:
    /// active loaders in the chain, plus active cells adjacent to any chain loader.
    /// </summary>
    public static List<int> AttachmentTerminals(
        HashSet<int> active,
        IReadOnlyList<int> chainLoaderIdx,
        List<int>[] neighbors)
    {
        var terminals = AttachmentSet(chainLoaderIdx, neighbors);
        terminals.RemoveWhere(i => !active.Contains(i));
        return [.. terminals.OrderBy(x => x)];
    }

    /// <summary>Index set of cells that are not masked as intakes.</summary>
    public static HashSet<int> IndicesWithoutMask(int n, bool[] intakeMask) =>
        Enumerable.Range(0, n).Where(i => !intakeMask[i]).ToHashSet();

    /// <summary>Steiner tree on terminals, then open faces along tree edges.</summary>
    public static bool TrySteinerOpen(
        HashSet<int> active,
        List<int>[] neighbors,
        List<(int Nb, int Dir)>[] neighborsDir,
        IReadOnlyList<int> terminals,
        out HashSet<int> tree,
        out Dictionary<int, CoolerFaceFlags> open)
    {
        open = new Dictionary<int, CoolerFaceFlags>();
        if (!SteinerTree(active, neighbors, terminals, out tree))
            return false;

        OpenAlongTree(tree, neighborsDir, open);
        return true;
    }

    /// <summary>
    /// One representative terminal per loader chain on the active deck set.
    /// Returns null when any chain has no attachment.
    /// </summary>
    public static List<int>? PickChainTerminals(
        HashSet<int> active,
        IReadOnlyList<IReadOnlyList<int>> chainLoaderIdx,
        List<int>[] neighbors)
    {
        var terminals = new List<int>();
        foreach (var chain in chainLoaderIdx)
        {
            var t = AttachmentTerminals(active, chain, neighbors);
            if (t.Count == 0)
                return null;

            int pick = chain.FirstOrDefault(active.Contains, t[0]);
            if (!active.Contains(pick))
                pick = t[0];
            terminals.Add(pick);
        }

        return terminals;
    }

    /// <summary>
    /// Approximate Steiner tree on <paramref name="terminals"/> via Prim growth:
    /// repeatedly attach the nearest remaining terminal to the current tree by a shortest path.
    /// </summary>
    public static bool SteinerTree(
        HashSet<int> active,
        List<int>[] neighbors,
        IReadOnlyList<int> terminals,
        out HashSet<int> tree)
    {
        tree = [];
        if (terminals.Count == 0)
            return false;
        if (terminals.Any(t => !active.Contains(t)))
            return false;

        var remaining = terminals.Distinct().OrderBy(t => t).ToHashSet();
        int start = remaining.Min();
        remaining.Remove(start);
        tree.Add(start);

        while (remaining.Count > 0)
        {
            var parent = new Dictionary<int, int>(tree.Count * 2);
            var queue = new Queue<int>();
            foreach (int source in tree)
            {
                parent[source] = source;
                queue.Enqueue(source);
            }

            int? hit = null;
            while (queue.Count > 0 && hit is null)
            {
                int cur = queue.Dequeue();
                foreach (int nb in neighbors[cur])
                {
                    if (!active.Contains(nb) || parent.ContainsKey(nb))
                        continue;

                    parent[nb] = cur;
                    if (remaining.Contains(nb))
                    {
                        hit = nb;
                        break;
                    }

                    queue.Enqueue(nb);
                }
            }

            if (hit is null)
                return false;

            int walk = hit.Value;
            while (true)
            {
                tree.Add(walk);
                if (parent[walk] == walk)
                    break;
                walk = parent[walk];
            }

            remaining.Remove(hit.Value);
        }

        return true;
    }

    public static void OpenAlongTree(
        HashSet<int> snake,
        List<(int Nb, int Dir)>[] neighborsDir,
        Dictionary<int, CoolerFaceFlags> open)
    {
        foreach (int i in snake)
        {
            open.TryGetValue(i, out var faces);
            foreach (var (nb, dir) in neighborsDir[i])
            {
                if (!snake.Contains(nb))
                    continue;
                faces = faces.With(dir);
                open.TryGetValue(nb, out var nbFaces);
                nbFaces = nbFaces.With(CoolerCardinals.Opposite[dir]);
                open[nb] = nbFaces;
            }

            open[i] = faces;
        }
    }

    public static void OpenTowardLoaders(
        HashSet<int> snake,
        IReadOnlyList<CellKey> cells,
        IReadOnlyList<CellKey> loaders,
        Dictionary<int, CoolerFaceFlags> open)
    {
        var loaderSet = loaders.ToHashSet();

        foreach (int i in snake)
        {
            open.TryGetValue(i, out var faces);
            var c = cells[i];
            for (int d = 0; d < 4; d++)
            {
                var (dr, dc) = CoolerCardinals.Offsets[d];
                var nb = new CellKey(c.Row + dr, c.Col + dc);
                if (!loaderSet.Contains(nb))
                    continue;

                faces = faces.With(d);
            }

            open[i] = faces;
        }
    }

    public static Dictionary<int, CoolerFaceFlags> OpenDeckFaces(
        HashSet<int> deckTree,
        CoolerRoutingContext ctx,
        PackingAnalysis packing)
    {
        var open = new Dictionary<int, CoolerFaceFlags>();
        OpenAlongTree(deckTree, ctx.NeighborsDir, open);
        OpenTowardLoaders(deckTree, ctx.Cells, packing.LoaderCells, open);
        return open;
    }

    /// <summary>
    /// Steiner-route a deck snake for the intake mask and materialize open faces + stage result.
    /// </summary>
    public static CoolerStageResult? TrySteinerDeckStage(
        CoolerRoutingContext ctx,
        PackingAnalysis packing,
        IReadOnlyList<EjectorCandidate> assignment,
        bool[] intakeMask,
        string detail,
        double solveSeconds = 0,
        IReadOnlyList<CellKey>? topIntakes = null)
    {
        var active = ctx.ActiveWithoutIntakes(intakeMask);
        var terminals = PickChainTerminals(active, ctx.ChainLoaderIdx, ctx.Neighbors);
        if (terminals is null)
            return null;

        if (!TrySteinerOpen(active, ctx.Neighbors, ctx.NeighborsDir, terminals, out var snake, out var openAlong))
            return null;

        OpenTowardLoaders(snake, ctx.Cells, packing.LoaderCells, openAlong);
        IReadOnlyList<CellKey> intakes = topIntakes ?? CollectTopIntakes(ctx, intakeMask);

        return CoolerStageResult.FromDeckRouting(
            detail, solveSeconds, assignment, ctx.Cells, intakes, snake, openAlong);
    }

    /// <summary>
    /// Try deck Steiner; on failure, elevate short bridges over intakes and return a 2-layer stage.
    /// </summary>
    public static CoolerStageResult? TrySteinerDeckOrBridge(
        CoolerRoutingContext ctx,
        PackingAnalysis packing,
        IReadOnlyList<EjectorCandidate> assignment,
        bool[] intakeMask,
        double solveSeconds = 0)
    {
        var deck = TrySteinerDeckStage(
            ctx, packing, assignment, intakeMask, CoolerStageDetails.DeckOnly, solveSeconds);
        if (deck is not null)
            return deck;

        var active = ctx.ActiveWithoutIntakes(intakeMask);
        var terminals = PickChainTerminals(active, ctx.ChainLoaderIdx, ctx.Neighbors);
        if (terminals is null)
            return null;

        if (!CoolerBridgeGraph.SteinerTreeWithIntakeBridges(
                ctx.CellCount, intakeMask, ctx.Neighbors, terminals,
                out var deckTree, out var bridgeTree)
            || bridgeTree.Count == 0)
            return null;

        var openDeck = OpenDeckFaces(deckTree, ctx, packing);
        var openBridge = new Dictionary<int, CoolerFaceFlags>();
        OpenAlongTree(bridgeTree, ctx.NeighborsDir, openBridge);

        return CoolerStageResult.FromDeckRouting(
            CoolerStageDetails.ElevatedBridges,
            solveSeconds,
            assignment,
            ctx.Cells,
            CollectTopIntakes(ctx, intakeMask),
            deckTree,
            openDeck,
            bridgeTree,
            openBridge,
            layers: 2);
    }

    public static List<CellKey> CollectTopIntakes(CoolerRoutingContext ctx, bool[] intakeMask) =>
        Enumerable.Range(0, intakeMask.Length)
            .Where(i => intakeMask[i])
            .Select(i => ctx.Cells[i])
            .OrderBy(c => c.Row)
            .ThenBy(c => c.Col)
            .ToList();

    public static Dictionary<int, int> LabelComponents(HashSet<int> active, List<int>[] neighbors)
    {
        var componentOf = new Dictionary<int, int>();
        int next = 0;
        foreach (int start in active.OrderBy(i => i))
        {
            if (componentOf.ContainsKey(start))
                continue;
            int id = next++;
            var q = new Queue<int>();
            q.Enqueue(start);
            componentOf[start] = id;
            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                foreach (int nb in neighbors[cur])
                {
                    if (!active.Contains(nb) || componentOf.ContainsKey(nb))
                        continue;
                    componentOf[nb] = id;
                    q.Enqueue(nb);
                }
            }
        }

        return componentOf;
    }
}
