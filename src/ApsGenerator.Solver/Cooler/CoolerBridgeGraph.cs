namespace ApsGenerator.Solver.Cooler;

/// <summary>Steiner routing with short elevated bridge segments over intakes.</summary>
internal static class CoolerBridgeGraph
{
    /// <summary>
    /// Connect loader-chain terminals on the deck when possible - use short
    /// hops linking distinct deck components. 
    /// </summary>
    public static bool SteinerTreeWithIntakeBridges(
        int n,
        bool[] intakeMask,
        List<int>[] deckNeighbors,
        IReadOnlyList<int> terminals,
        out HashSet<int> deckTree,
        out HashSet<int> bridgeTree)
    {
        deckTree = [];
        bridgeTree = [];
        if (terminals.Count == 0)
            return false;

        var deckActive = CoolerGraph.IndicesWithoutMask(n, intakeMask);
        if (terminals.Any(t => !deckActive.Contains(t)))
            return false;

        if (CoolerGraph.SteinerTree(deckActive, deckNeighbors, terminals, out deckTree))
            return true;

        var componentOf = CoolerGraph.LabelComponents(deckActive, deckNeighbors);
        var terminalComponents = terminals
            .Select(t => componentOf[t])
            .Distinct()
            .OrderBy(c => c)
            .ToList();
        if (terminalComponents.Count <= 1)
            return false;

        if (!TryChooseBridgeMst(n, deckNeighbors, componentOf, terminalComponents, deckActive, out var chosenBridgeNodes))
            return false;

        return BuildDeckAndBridgeTrees(
            n,
            intakeMask,
            deckNeighbors,
            terminals,
            deckActive,
            componentOf,
            terminalComponents,
            chosenBridgeNodes,
            out deckTree,
            out bridgeTree);
    }

    private static bool TryChooseBridgeMst(
        int n,
        List<int>[] deckNeighbors,
        Dictionary<int, int> componentOf,
        List<int> terminalComponents,
        HashSet<int> deckActive,
        out HashSet<int> chosenBridgeNodes)
    {
        chosenBridgeNodes = [];
        var portalComponent = new Dictionary<int, int>();
        foreach (int deck in deckActive)
            portalComponent[deck + n] = componentOf[deck];

        var pairPath = BuildPairBridgePaths(n, deckNeighbors, terminalComponents, portalComponent);
        if (pairPath is null)
            return false;

        var inMst = new HashSet<int> { terminalComponents[0] };
        while (inMst.Count < terminalComponents.Count)
        {
            List<int>? bestPath = null;
            int bestOther = -1;
            int bestAttach = int.MinValue;
            foreach (int ca in inMst)
            {
                foreach (int cb in terminalComponents)
                {
                    if (inMst.Contains(cb))
                        continue;
                    var path = pairPath[(ca, cb)];
                    int attach = PortalAttachmentScore(
                        path, n, deckNeighbors, portalComponent, ca, cb);
                    if (bestPath is not null
                        && (path.Count > bestPath.Count
                            || (path.Count == bestPath.Count && attach <= bestAttach)))
                        continue;

                    bestPath = path;
                    bestOther = cb;
                    bestAttach = attach;
                }
            }

            if (bestPath is null || bestOther < 0)
                return false;

            foreach (int bridgeNode in bestPath)
                chosenBridgeNodes.Add(bridgeNode);
            inMst.Add(bestOther);
        }

        return true;
    }

    private static Dictionary<(int Ca, int Cb), List<int>>? BuildPairBridgePaths(
        int n,
        List<int>[] deckNeighbors,
        List<int> terminalComponents,
        Dictionary<int, int> portalComponent)
    {
        var portalsByComponent = new Dictionary<int, List<int>>();
        foreach (var (portal, component) in portalComponent)
        {
            if (!portalsByComponent.TryGetValue(component, out var list))
            {
                list = [];
                portalsByComponent[component] = list;
            }

            list.Add(portal);
        }

        var pairPath = new Dictionary<(int Ca, int Cb), List<int>>();
        for (int a = 0; a < terminalComponents.Count; a++)
        {
            int ca = terminalComponents[a];
            if (!portalsByComponent.TryGetValue(ca, out var sources) || sources.Count == 0)
                return null;

            var (parent, sourceOf) = BridgeBfsFromPortals(n, sources, deckNeighbors);
            for (int b = a + 1; b < terminalComponents.Count; b++)
            {
                int cb = terminalComponents[b];
                if (!portalsByComponent.TryGetValue(cb, out var targets))
                    return null;

                List<int>? best = null;
                int bestAttach = int.MinValue;
                foreach (int target in targets)
                {
                    if (!parent.ContainsKey(target))
                        continue;
                    var path = ReconstructBridgePath(target, parent, sourceOf);
                    int attach = PortalAttachmentScore(path, n, deckNeighbors, portalComponent, ca, cb);
                    if (best is not null
                        && (path.Count > best.Count
                            || (path.Count == best.Count && attach <= bestAttach)))
                        continue;

                    best = path;
                    bestAttach = attach;
                }

                if (best is null)
                    return null;
                pairPath[(ca, cb)] = best;
                pairPath[(cb, ca)] = best;
            }
        }

        return pairPath;
    }

    private static bool BuildDeckAndBridgeTrees(
        int n,
        bool[] intakeMask,
        List<int>[] deckNeighbors,
        IReadOnlyList<int> terminals,
        HashSet<int> deckActive,
        Dictionary<int, int> componentOf,
        List<int> terminalComponents,
        HashSet<int> chosenBridgeNodes,
        out HashSet<int> deckTree,
        out HashSet<int> bridgeTree)
    {
        var rampDecksByComponent = new Dictionary<int, HashSet<int>>();
        foreach (int bridgeNode in chosenBridgeNodes)
        {
            int cell = bridgeNode - n;
            if (intakeMask[cell])
                continue;
            if (!componentOf.TryGetValue(cell, out int comp))
                continue;
            if (!rampDecksByComponent.TryGetValue(comp, out var set))
            {
                set = [];
                rampDecksByComponent[comp] = set;
            }

            set.Add(cell);
        }

        deckTree = [];
        foreach (int comp in terminalComponents)
        {
            var active = deckActive.Where(i => componentOf[i] == comp).ToHashSet();
            var localTerminals = terminals.Where(t => componentOf[t] == comp).ToList();
            if (rampDecksByComponent.TryGetValue(comp, out var ramps))
            {
                foreach (int r in ramps)
                {
                    if (!localTerminals.Contains(r))
                        localTerminals.Add(r);
                }
            }

            if (localTerminals.Count == 0)
                continue;
            if (!CoolerGraph.SteinerTree(active, deckNeighbors, localTerminals, out var local))
            {
                bridgeTree = [];
                return false;
            }

            foreach (int i in local)
                deckTree.Add(i);
        }

        bridgeTree = [];
        foreach (int bridgeNode in chosenBridgeNodes)
            bridgeTree.Add(bridgeNode - n);

        return deckTree.Count > 0 && bridgeTree.Count > 0;
    }

    /// <summary>
    /// Prefer bridge portals that sit deeper in their own deck component (more local snake
    /// neighbors). Breaks ties toward attachments that meet an existing corridor rather than
    /// dangling through a side clip.
    /// </summary>
    private static int PortalAttachmentScore(
        List<int> path,
        int n,
        List<int>[] deckNeighbors,
        Dictionary<int, int> portalComponent,
        int componentA,
        int componentB)
    {
        if (path.Count == 0)
            return int.MinValue;

        int score = 0;
        foreach (int elevated in new[] { path[0], path[^1] })
        {
            if (!portalComponent.TryGetValue(elevated, out int comp))
                continue;
            if (comp != componentA && comp != componentB)
                continue;

            int cell = elevated - n;
            foreach (int nb in deckNeighbors[cell])
            {
                int nbElev = nb + n;
                if (portalComponent.TryGetValue(nbElev, out int nbComp) && nbComp == comp)
                    score++;
            }
        }

        return score;
    }

    private static (Dictionary<int, int> Parent, Dictionary<int, int> SourceOf) BridgeBfsFromPortals(
        int n,
        IReadOnlyList<int> sources,
        List<int>[] deckNeighbors)
    {
        var parent = new Dictionary<int, int>();
        var sourceOf = new Dictionary<int, int>();
        var q = new Queue<int>();
        foreach (int s in sources)
        {
            parent[s] = s;
            sourceOf[s] = s;
            q.Enqueue(s);
        }

        while (q.Count > 0)
        {
            int cur = q.Dequeue();
            int cell = cur - n;
            foreach (int nbDeck in deckNeighbors[cell])
            {
                int nb = nbDeck + n;
                if (parent.ContainsKey(nb))
                    continue;
                parent[nb] = cur;
                sourceOf[nb] = sourceOf[cur];
                q.Enqueue(nb);
            }
        }

        return (parent, sourceOf);
    }

    private static List<int> ReconstructBridgePath(
        int target,
        Dictionary<int, int> parent,
        Dictionary<int, int> sourceOf)
    {
        var path = new List<int>();
        int cur = target;
        int source = sourceOf[target];
        while (true)
        {
            path.Add(cur);
            if (cur == source)
                break;
            cur = parent[cur];
        }

        path.Reverse();
        return path;
    }
}
