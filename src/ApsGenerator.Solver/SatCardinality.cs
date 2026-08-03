using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

internal static class SatCardinality
{
    public static void AddExactK(
        SatSolver solver,
        IReadOnlyList<int> lits,
        int k,
        ref int nextVar,
        ref int clauseCount)
    {
        if (k < 0 || k > lits.Count)
        {
            AddContradiction(solver, ref nextVar, ref clauseCount);
            return;
        }

        AddAtMostK(solver, lits, k, ref nextVar, ref clauseCount);
        AddAtLeastK(solver, lits, k, ref nextVar, ref clauseCount);
    }

    public static void AddAtLeastK(
        SatSolver solver,
        IReadOnlyList<int> lits,
        int k,
        ref int nextVar,
        ref int clauseCount)
    {
        if (k <= 0)
            return;
        if (k > lits.Count)
        {
            AddContradiction(solver, ref nextVar, ref clauseCount);
            return;
        }

        // At least K ≡ at most (n−K) of the negated lits
        var negs = lits.Select(l => -l).ToList();
        AddAtMostK(solver, negs, lits.Count - k, ref nextVar, ref clauseCount);
    }

    /// <summary>Sinz sequential counter at-most-K.</summary>
    public static void AddAtMostK(
        SatSolver solver,
        IReadOnlyList<int> lits,
        int k,
        ref int nextVar)
    {
        int ignored = 0;
        AddAtMostK(solver, lits, k, ref nextVar, ref ignored);
    }

    /// <summary>Sinz sequential counter at-most-K.</summary>
    public static void AddAtMostK(
        SatSolver solver,
        IReadOnlyList<int> lits,
        int k,
        ref int nextVar,
        ref int clauseCount)
    {
        EncodeSinzAtMostK(
            solver, lits, k, ref nextVar, ref clauseCount,
            gateSelector: 0);
    }

    /// <summary>
    /// Exact-K constrained by assumption literal <paramref name="selector"/>:
    /// when selector is assumed true, exactly K of <paramref name="lits"/> are true.
    /// </summary>
    public static void AddExactKImpliedBy(
        SatSolver solver,
        int selector,
        IReadOnlyList<int> lits,
        int k,
        ref int nextVar,
        ref int clauseCount)
    {
        if (k < 0 || k > lits.Count)
        {
            // selector ⇒ false
            solver.AddClause([-selector]);
            clauseCount++;
            return;
        }

        AddAtMostKImpliedBy(solver, selector, lits, k, ref nextVar, ref clauseCount);
        AddAtLeastKImpliedBy(solver, selector, lits, k, ref nextVar, ref clauseCount);
    }

    private static void AddAtLeastKImpliedBy(
        SatSolver solver,
        int selector,
        IReadOnlyList<int> lits,
        int k,
        ref int nextVar,
        ref int clauseCount)
    {
        if (k <= 0)
            return;
        if (k > lits.Count)
        {
            solver.AddClause([-selector]);
            clauseCount++;
            return;
        }

        var negs = lits.Select(l => -l).ToList();
        AddAtMostKImpliedBy(solver, selector, negs, lits.Count - k, ref nextVar, ref clauseCount);
    }

    private static void AddAtMostKImpliedBy(
        SatSolver solver,
        int selector,
        IReadOnlyList<int> lits,
        int k,
        ref int nextVar,
        ref int clauseCount)
    {
        EncodeSinzAtMostK(
            solver, lits, k, ref nextVar, ref clauseCount,
            gateSelector: selector);
    }

    /// <summary>
    /// Shared Sinz sequential-counter at-most-K.
    /// When <paramref name="gateSelector"/> is 0, clauses are emitted directly;
    /// otherwise each clause is gated as (~selector ∨ clause), and k&lt;0 / k=0
    /// cases also include the selector.
    /// </summary>
    private static void EncodeSinzAtMostK(
        SatSolver solver,
        IReadOnlyList<int> lits,
        int k,
        ref int nextVar,
        ref int clauseCount,
        int gateSelector)
    {
        bool gated = gateSelector != 0;

        if (k < 0)
        {
            if (gated)
            {
                solver.AddClause([-gateSelector]);
                clauseCount++;
            }
            else
            {
                AddContradiction(solver, ref nextVar, ref clauseCount);
            }

            return;
        }

        if (k >= lits.Count)
            return;

        if (k == 0)
        {
            foreach (int lit in lits)
            {
                if (gated)
                    solver.AddClause([-gateSelector, -lit]);
                else
                    solver.AddClause([-lit]);
                clauseCount++;
            }

            return;
        }

        int n = lits.Count;
        var s = new int[n, k];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < k; j++)
            {
                solver.AddVariables(1);
                s[i, j] = ++nextVar;
            }

        int localClauses = 0;
        void Add(params int[] clause)
        {
            if (gated)
            {
                var gatedClause = new int[clause.Length + 1];
                gatedClause[0] = -gateSelector;
                clause.CopyTo(gatedClause, 1);
                solver.AddClause(gatedClause);
            }
            else
            {
                solver.AddClause(clause);
            }

            localClauses++;
        }

        Add(-lits[0], s[0, 0]);
        for (int j = 1; j < k; j++)
            Add(-s[0, j]);

        for (int i = 1; i < n; i++)
        {
            Add(-lits[i], s[i, 0]);
            Add(-s[i - 1, 0], s[i, 0]);
            for (int j = 1; j < k; j++)
            {
                Add(-lits[i], -s[i - 1, j - 1], s[i, j]);
                Add(-s[i - 1, j], s[i, j]);
            }

            Add(-lits[i], -s[i - 1, k - 1]);
        }

        clauseCount += localClauses;
    }

    public static void AddContradiction(SatSolver solver, ref int nextVar, ref int clauseCount)
    {
        solver.AddVariables(1);
        int v = ++nextVar;
        solver.AddClause([v]);
        solver.AddClause([-v]);
        clauseCount += 2;
    }
}
