namespace ApsGenerator.Solver.Cooler;

/// <summary>Undirected reachability CNF helpers for cooler SAT.</summary>
internal static class CoolerReachability
{
    public static int[][] AllocateDepthVars(SatClauseBuilder builder, int cellCount, int maxDepth)
    {
        var reach = new int[cellCount][];
        for (int i = 0; i < cellCount; i++)
            reach[i] = builder.NewVars(maxDepth + 1);
        return reach;
    }

    /// <summary>
    /// Undirected step: reach[i][d] ⇒ ∨ reach[nb][d−1] over undirected neighbors.
    /// </summary>
    public static void EncodeUndirectedSteps(
        SatClauseBuilder builder,
        int[][] reach,
        List<int>[] neighbors,
        int maxDepth)
    {
        int n = reach.Length;
        // Degree ≤ 4 on the grid graph: -reach[i][d] + up to 4 neighbors.
        Span<int> lits = stackalloc int[5];
        for (int i = 0; i < n; i++)
        {
            for (int d = 1; d <= maxDepth; d++)
            {
                int count = 0;
                lits[count++] = -reach[i][d];
                foreach (int nb in neighbors[i])
                    lits[count++] = reach[nb][d - 1];

                if (count == 1)
                    builder.Add(-reach[i][d]);
                else
                    builder.Add(lits[..count]);
            }
        }
    }

    /// <summary>At least one cell in <paramref name="attach"/> is reached at some depth.</summary>
    public static void EncodeAttachmentReached(
        SatClauseBuilder builder,
        int[][] reach,
        IEnumerable<int> attach,
        int maxDepth)
    {
        var lits = new List<int>();
        foreach (int i in attach)
        {
            for (int d = 0; d <= maxDepth; d++)
                lits.Add(reach[i][d]);
        }

        if (lits.Count == 0)
        {
            builder.AddContradiction();
            return;
        }

        builder.Add([.. lits]);
    }

    public static void FixSingleRoot(SatClauseBuilder builder, int[][] reach, int root)
    {
        builder.Add(reach[root][0]);
        for (int i = 0; i < reach.Length; i++)
        {
            if (i == root)
                continue;
            builder.Add(-reach[i][0]);
        }
    }
}
