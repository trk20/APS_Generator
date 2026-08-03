using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

internal static class EjectorAssignmentSearch
{
    public static IEnumerable<IReadOnlyList<EjectorCandidate>> ForConstructive(
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog)
    {
        var preferred = BuildVerticalPreferThenBottom(catalog, out bool preferredOk);
        if (preferredOk)
            yield return preferred;

        var greedy = BuildMinDeficitGreedy(catalog, out bool greedyOk);
        if (greedyOk && AssignmentsDiffer(preferred, greedy))
            yield return greedy;
        else if (greedyOk && !preferredOk)
            yield return greedy;

        var rng = new Random(CoolerSolverConstants.ConstructiveRandomSeed);
        for (int t = 0; t < CoolerSolverConstants.ConstructiveRandomTrials; t++)
        {
            if (TryRandomSample(catalog, rng, out var sample))
                yield return sample;
        }
    }

    public static IEnumerable<IReadOnlyList<EjectorCandidate>> ForUndirected(
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        int? randomTrials = null)
    {
        var bottoms = BuildAllBottom(catalog, out bool bottomsOk);
        if (bottomsOk)
            yield return bottoms;

        var greedy = BuildMinDeficitGreedy(catalog, out bool greedyOk);
        if (greedyOk && AssignmentsDiffer(bottoms, greedy))
            yield return greedy;

        int trials = randomTrials ?? CoolerSolverConstants.UndirectedRandomTrials;
        var rng = new Random(CoolerSolverConstants.UndirectedRandomSeed);
        for (int t = 0; t < trials; t++)
        {
            if (TryRandomSample(catalog, rng, out var sample))
                yield return sample;
        }
    }

    public static IEnumerable<IReadOnlyList<EjectorCandidate>> ForBridge(
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog)
    {
        var preferred = BuildVerticalPreferThenBottom(catalog, out bool preferredOk);
        if (preferredOk)
            yield return preferred;

        var bottoms = BuildAllBottom(catalog, out bool bottomsOk);
        if (bottomsOk && AssignmentsDiffer(preferred, bottoms))
            yield return bottoms;

        var greedy = BuildMinDeficitGreedy(catalog, out bool greedyOk);
        if (greedyOk && AssignmentsDiffer(preferred, greedy) && AssignmentsDiffer(bottoms, greedy))
            yield return greedy;
        else if (greedyOk && !preferredOk && !bottomsOk)
            yield return greedy;
    }

    private static List<EjectorCandidate> BuildVerticalPreferThenBottom(
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        out bool ok) =>
        TryBuildAssignment(
            catalog,
            claimed => options =>
                options.FirstOrDefault(c =>
                    c.Kind == EjectorKind.VerticalOpenArmDown && !claimed.Contains(c.Protrusion))
                ?? options.FirstOrDefault(c =>
                    c.Kind == EjectorKind.Bottom && !claimed.Contains(c.Protrusion))
                ?? options.FirstOrDefault(c =>
                    c.Kind == EjectorKind.None && !claimed.Contains(c.Protrusion)),
            out ok);

    private static List<EjectorCandidate> BuildAllBottom(
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        out bool ok) =>
        TryBuildAssignment(
            catalog,
            claimed => options => options
                .Where(c =>
                    (c.Kind == EjectorKind.Bottom || c.Kind == EjectorKind.None)
                    && !claimed.Contains(c.Protrusion))
                .OrderBy(c => c.TopDeficit)
                .FirstOrDefault(),
            out ok);

    private static List<EjectorCandidate> BuildMinDeficitGreedy(
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        out bool ok) =>
        TryBuildAssignment(
            catalog,
            claimed => options => options
                .Where(c => !claimed.Contains(c.Protrusion))
                .OrderBy(c => c.TopDeficit)
                .ThenBy(c => c.Kind switch
                {
                    EjectorKind.VerticalOpenArmDown => 0,
                    EjectorKind.None => 1,
                    _ => 2,
                })
                .FirstOrDefault(),
            out ok);

    private static List<EjectorCandidate> TryBuildAssignment(
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        Func<HashSet<CellKey>, Func<IReadOnlyList<EjectorCandidate>, EjectorCandidate?>> pickFactory,
        out bool ok)
    {
        var result = new List<EjectorCandidate>(catalog.Count);
        var claimed = new HashSet<CellKey>();
        var pick = pickFactory(claimed);
        ok = true;
        foreach (var options in catalog)
        {
            var chosen = pick(options);
            if (chosen is null)
            {
                ok = false;
                break;
            }

            result.Add(chosen);
            claimed.Add(chosen.Protrusion);
        }

        if (!ok || result.Count != catalog.Count)
            ok = false;

        return result;
    }

    private static bool TryRandomSample(
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        Random rng,
        out IReadOnlyList<EjectorCandidate> sample)
    {
        var claimed = new HashSet<CellKey>();
        var list = new List<EjectorCandidate>(catalog.Count);
        foreach (var options in catalog)
        {
            var avail = options.Where(c => !claimed.Contains(c.Protrusion)).ToList();
            if (avail.Count == 0)
            {
                sample = list;
                return false;
            }

            var pick = avail[rng.Next(avail.Count)];
            list.Add(pick);
            claimed.Add(pick.Protrusion);
        }

        sample = list;
        return true;
    }

    private static bool AssignmentsDiffer(
        IReadOnlyList<EjectorCandidate> a,
        IReadOnlyList<EjectorCandidate> b) =>
        a.Count != b.Count
        || a.Zip(b).Any(p => p.First.Kind != p.Second.Kind
            || !p.First.Protrusion.Equals(p.Second.Protrusion));
}
