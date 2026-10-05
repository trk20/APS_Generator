using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver.Tests;

public sealed class SatTotalizerTests
{
    [Fact]
    public void EverySmallAssignmentAgreesWithItsPopulationCount()
    {
        const int maximumInputs = 7;
        for (int count = 0; count <= maximumInputs; count++)
        {
            var inputs = Enumerable.Range(1, count).ToArray();
            for (int bound = -1; bound <= count + 1; bound++)
                AssertAssignments(inputs, count, bound);
        }
    }

    [Fact]
    public void RepeatedAndNegatedInputsRetainTheirMultiplicity()
    {
        int[] inputs = [1, 1, -2, 3, -3];
        for (int bound = -1; bound <= inputs.Length + 1; bound++)
            AssertAssignments(inputs, 3, bound);
    }

    [Fact]
    public void WeightedLeavesAgreeWithTheirExpandedPopulationCount()
    {
        WeightedLiteral[] terms = [new(1, 1), new(-2, 2), new(3, 4)];
        int[] inputs = terms.SelectMany(term => Enumerable.Repeat(term.Literal, term.Weight)).ToArray();
        for (int bound = -1; bound <= inputs.Length + 1; bound++)
        {
            using var solver = new SatSolver();
            solver.AddVariables(3);
            int nextVariable = 3;
            SatTotalizer.AddWeightedAtMostK(solver, terms, bound, ref nextVariable);
            AssertModels(solver, inputs, 3, bound);
        }
    }

    [Fact]
    public void WeightedCounterKeepsEveryBoundUnderChangingAssumptions()
    {
        WeightedLiteral[] terms = [new(1, 1), new(1, 1), new(-2, 2), new(3, 4)];
        using var solver = new SatSolver();
        solver.AddVariables(3);
        int nextVariable = 3;
        var root = SatTotalizer.BuildAtMostKCounter(solver, terms, 8, ref nextVariable);
        foreach (int bound in new[] { 8, 0, 4, 2, 7, 1, 6, 3, 5 })
            for (int assignment = 0; assignment < 8; assignment++)
            {
                var assumptions = Enumerable.Range(1, 3)
                    .Select(variable => IsTrue(variable, assignment) ? variable : -variable).ToList();
                int? threshold = SatTotalizer.GetAtMostKAssumption(root, bound);
                if (threshold.HasValue)
                    assumptions.Add(threshold.Value);
                int population = terms.Where(term => IsTrue(term.Literal, assignment)).Sum(term => term.Weight);
                Assert.Equal(population <= bound ? CryptoMiniSatNative.Lbool.True : CryptoMiniSatNative.Lbool.False,
                    solver.SolveWithAssumptions(assumptions.ToArray()));
            }
    }

    private static void AssertAssignments(int[] inputs, int variables, int bound)
    {
        using var solver = new SatSolver();
        solver.AddVariables(variables);
        int nextVariable = variables;
        SatTotalizer.AddAtMostK(solver, inputs, bound, ref nextVariable);
        AssertModels(solver, inputs, variables, bound);
    }

    private static void AssertModels(SatSolver solver, int[] inputs, int variables, int bound)
    {
        for (int assignment = 0; assignment < 1 << variables; assignment++)
        {
            var assumptions = Enumerable.Range(1, variables)
                .Select(variable => IsTrue(variable, assignment) ? variable : -variable).ToArray();
            bool expected = inputs.Count(literal => IsTrue(literal, assignment)) <= bound;
            var result = solver.SolveWithAssumptions(assumptions);
            Assert.True(result == (expected ? CryptoMiniSatNative.Lbool.True : CryptoMiniSatNative.Lbool.False),
                $"Inputs=[{string.Join(",", inputs)}], bound={bound}, assignment={assignment}, result={result}");
        }
    }

    private static bool IsTrue(int literal, int assignment) =>
        ((assignment & (1 << (Math.Abs(literal) - 1))) != 0) == (literal > 0);
}
