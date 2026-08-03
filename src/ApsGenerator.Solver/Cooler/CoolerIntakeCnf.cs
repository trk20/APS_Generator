namespace ApsGenerator.Solver.Cooler;

/// <summary>Top-intake CNF for the undirected cooler SAT path.</summary>
internal static class CoolerIntakeCnf
{
    /// <summary>
    /// Shared intake variables + exclusive bans. Call once per undirected solve session.
    /// </summary>
    public static int[] AllocateIntakeVars(SatClauseBuilder builder, CoolerRoutingContext ctx)
    {
        int[] intakeVars = builder.NewVars(ctx.CellCount);
        builder.Solver.SetDefaultPolarity(false);

        for (int i = 0; i < ctx.CellCount; i++)
        {
            if (!ctx.ExclusiveSet.Contains(ctx.Cells[i]))
                builder.Add(-intakeVars[i]);
        }

        return intakeVars;
    }

    /// <summary>
    /// Pre-encode Exact-K intake quotas for every TopDeficit that appears in the catalog,
    /// gated by selector literals for assumption-based multi-assignment solves.
    /// </summary>
    public static Dictionary<(int ClusterIndex, int TopDeficit), int> EncodeCatalogSelectors(
        SatClauseBuilder builder,
        CoolerRoutingContext ctx,
        int[] intakeVars,
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog)
    {
        var selectors = new Dictionary<(int, int), int>();

        foreach (var cluster in ctx.Packing.Clusters)
        {
            var deficits = catalog[cluster.Index]
                .Select(c => c.TopDeficit)
                .Distinct()
                .OrderBy(k => k);

            var lits = cluster.Footprint
                .Where(ctx.ExclusiveSet.Contains)
                .Select(cell => intakeVars[ctx.IndexOf[cell]])
                .ToList();

            int nonLoader = cluster.Footprint.Count - 1;
            int loaderLit = intakeVars[ctx.IndexOf[cluster.Loader]];

            foreach (int k in deficits)
            {
                int selector = builder.NewVar();
                selectors[(cluster.Index, k)] = selector;
                SatCardinality.AddExactKImpliedBy(
                    builder.Solver, selector, lits, k,
                    ref builder.NextVarRef, ref builder.ClauseCountRef);

                if (nonLoader >= k && k > 0)
                    // Ban loader on top intake when non-loader cells can cover K
                    builder.Add(-selector, -loaderLit);
            }
        }

        return selectors;
    }
}
