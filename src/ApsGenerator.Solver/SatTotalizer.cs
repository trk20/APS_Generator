using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

internal readonly record struct WeightedLiteral(int Literal, int Weight);

/// <summary>
/// Balanced unary counters, truncated at the requested upper bound.
/// Only forward implications are needed: true inputs force their count outputs.
/// </summary>
internal static class SatTotalizer
{
    /// <summary>
    /// Builds a truncated unary counter whose root outputs can be guarded by
    /// assumptions for any bound from zero through <paramref name="maximumBound"/>.
    /// Root output i means that at least i+1 weighted inputs are true.
    /// </summary>
    public static int[] BuildAtMostKCounter(SatSolver solver, IReadOnlyList<int> literals,
        int maximumBound, ref int nextVariable)
    {
        var terms = literals.Select(literal => new WeightedLiteral(literal, 1)).ToArray();
        return BuildAtMostKCounter(solver, terms, maximumBound, ref nextVariable);
    }

    /// <summary>
    /// Builds a weighted unary counter. To impose an at-most bound K, pass
    /// assumption -root[K]. If the requested maximum is at least the total
    /// weight, the complete root is retained so later tighter assumptions can
    /// still be applied.
    /// </summary>
    public static int[] BuildAtMostKCounter(SatSolver solver,
        IReadOnlyList<WeightedLiteral> terms, int maximumBound, ref int nextVariable)
    {
        if (maximumBound < 0)
        {
            solver.AddClause([]);
            return [];
        }

        var remaining = new List<WeightedLiteral>();
        foreach (var term in terms)
        {
            if (term.Weight > maximumBound)
                solver.AddClause([-term.Literal]);
            else
                remaining.Add(term);
        }

        if (remaining.Count == 0)
            return [];

        // The extra output is the threshold that enforces ≤ maximumBound.
        int totalWeight = remaining.Sum(term => term.Weight);
        int outputCount = Math.Min(maximumBound + 1, totalWeight);
        return BuildCounter(solver, remaining, 0, remaining.Count,
            outputCount, ref nextVariable);
    }

    /// <summary>
    /// Returns the assumption that restricts a root counter to at most bound.
    /// A null result means the bound is redundant for this counter.
    /// </summary>
    public static int? GetAtMostKAssumption(IReadOnlyList<int> root, int bound)
    {
        if (bound < 0)
            throw new ArgumentOutOfRangeException(nameof(bound));
        return bound < root.Count ? -root[bound] : null;
    }

    public static void AddAtMostK(SatSolver solver, IReadOnlyList<int> literals, int bound, ref int nextVariable)
    {
        var terms = literals.Select(literal => new WeightedLiteral(literal, 1)).ToArray();
        AddWeightedAtMostK(solver, terms, bound, ref nextVariable);
    }

    public static void AddWeightedAtMostK(SatSolver solver, IReadOnlyList<WeightedLiteral> terms,
        int bound, ref int nextVariable)
    {
        if (bound < 0)
        {
            solver.AddClause([]);
            return;
        }
        if (bound >= terms.Sum(term => term.Weight))
            return;

        var remaining = new List<WeightedLiteral>();
        foreach (var term in terms)
        {
            if (term.Weight > bound)
                solver.AddClause([-term.Literal]);
            else
                remaining.Add(term);
        }
        if (remaining.Count > 0)
            BuildCounter(solver, remaining, 0, remaining.Count, bound, ref nextVariable);
    }

    private static int[] BuildCounter(SatSolver solver, IReadOnlyList<WeightedLiteral> terms,
        int start, int count, int bound, ref int nextVariable)
    {
        if (count == 1)
            return Enumerable.Repeat(terms[start].Literal, terms[start].Weight).ToArray();

        int leftCount = count / 2;
        var left = BuildCounter(solver, terms, start, leftCount, bound, ref nextVariable);
        var right = BuildCounter(solver, terms, start + leftCount, count - leftCount, bound, ref nextVariable);
        return MergeCounters(solver, left, right, bound, ref nextVariable);
    }

    private static int[] MergeCounters(SatSolver solver, int[] left, int[] right,
        int bound, ref int nextVariable)
    {
        int size = Math.Min(left.Length + right.Length, bound);
        solver.AddVariables(size);
        var outputs = new int[size];
        for (int index = 0; index < size; index++)
            outputs[index] = ++nextVariable;

        for (int index = 0; index < left.Length; index++)
            solver.AddClause([-left[index], outputs[index]]);
        for (int index = 0; index < right.Length; index++)
            solver.AddClause([-right[index], outputs[index]]);
        for (int first = 0; first < left.Length; first++)
        {
            for (int second = 0; second < right.Length; second++)
            {
                int total = first + second + 2;
                if (total > bound)
                {
                    // A larger right count also forces this first overflowing threshold.
                    solver.AddClause([-left[first], -right[second]]);
                    break;
                }
                solver.AddClause([-left[first], -right[second], outputs[total - 1]]);
            }
        }
        return outputs;
    }
}
