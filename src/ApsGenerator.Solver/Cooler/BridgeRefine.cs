using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

/// <summary>
/// Post-process bridged cooler solutions by moving top intakes off bridge corridors
/// onto free clips (preferring clips that already touch the deck snake).
/// </summary>
internal static class BridgeRefine
{
    public static CoolerStageResult Improve(
        CoolerRoutingContext ctx,
        PackingAnalysis packing,
        IReadOnlyList<EjectorCandidate> assignment,
        CoolerStageResult current,
        bool[] intakeMask)
    {
        if (current.BridgeCoolerCells.Count == 0)
            return current;

        var best = current;
        var bestMask = (bool[])intakeMask.Clone();
        var trial = new bool[intakeMask.Length];

        for (int pass = 0; pass < CoolerSolverConstants.BridgeRefineMaxPasses; pass++)
        {
            int bestBridges = best.BridgeCoolerCells.Count;
            int compsBefore = CoolerIntakeAssign.CountTerminalComponents(ctx, bestMask);
            bool improved = false;

            foreach (var (from, to) in EnumerateBridgeIntakeMoves(ctx, packing, bestMask, best))
            {
                if (!bestMask[from] || bestMask[to])
                    continue;

                Array.Copy(bestMask, trial, trial.Length);
                trial[from] = false;
                trial[to] = true;

                // Skip Steiner when the move cannot reduce terminal fragmentation.
                if (CoolerIntakeAssign.CountTerminalComponents(ctx, trial) > compsBefore)
                    continue;

                var candidate = CoolerGraph.TrySteinerDeckOrBridge(
                    ctx, packing, assignment, trial);
                if (candidate is null)
                    continue;

                if (candidate.BridgeCoolerCells.Count >= bestBridges)
                    continue;

                best = candidate;
                Array.Copy(trial, bestMask, bestMask.Length);
                improved = true;
                break; // first improvement; re-enumerate from the new mask
            }

            if (!improved || best.BridgeCoolerCells.Count == 0)
                break;
        }

        return best;
    }

    /// <summary>
    /// Only moves that clear an intake sitting on a bridge cell. 
    /// Try destinations that touch the foreign deck snake first (attachment repair).
    /// </summary>
    private static IEnumerable<(int From, int To)> EnumerateBridgeIntakeMoves(
        CoolerRoutingContext ctx,
        PackingAnalysis packing,
        bool[] intakeMask,
        CoolerStageResult current)
    {
        var bridgeXy = current.BridgeCoolerCells.ToHashSet();
        var deckXy = current.CoolerCells.ToHashSet();
        var moves = new List<(int From, int To, int Priority)>();

        foreach (var cluster in packing.Clusters)
        {
            var bridgeIntakes = cluster.Footprint
                .Where(c => bridgeXy.Contains(c)
                            && ctx.IndexOf.TryGetValue(c, out int i)
                            && intakeMask[i])
                .ToList();
            if (bridgeIntakes.Count == 0)
                continue;

            var freeClips = cluster.Clips
                .Where(c => ctx.IndexOf.TryGetValue(c, out int i) && !intakeMask[i] && !bridgeXy.Contains(c))
                .ToList();
            if (freeClips.Count == 0)
                continue;

            foreach (var intake in bridgeIntakes)
            {
                int from = ctx.IndexOf[intake];
                foreach (var dest in freeClips)
                {
                    int to = ctx.IndexOf[dest];
                    bool touchesDeck = dest.CardinalNeighbors().Any(nb =>
                        deckXy.Contains(nb) && !cluster.Footprint.Contains(nb));
                    moves.Add((from, to, touchesDeck ? 0 : 1));
                }
            }
        }

        return moves
            .OrderBy(m => m.Priority)
            .ThenBy(m => m.From)
            .ThenBy(m => m.To)
            .Take(CoolerSolverConstants.BridgeRefineMaxMovesPerPass)
            .Select(m => (m.From, m.To));
    }
}
