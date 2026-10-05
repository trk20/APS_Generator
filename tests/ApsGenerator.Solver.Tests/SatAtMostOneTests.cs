using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver.Tests;

public sealed class SatAtMostOneTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(24)]
    public void EveryAllowedChoiceAndEveryPairMatchesAtMostOne(int count)
    {
        using var solver = new SatSolver();
        solver.AddVariables(count);
        int nextVariable = count;
        int[] inputs = Enumerable.Range(1, count).ToArray();
        SatAtMostOne.Add(solver, inputs, ref nextVariable);
        Assert.Equal(CryptoMiniSatNative.Lbool.True, solver.SolveWithAssumptions(inputs.Select(x => -x).ToArray()));
        for (int first = 0; first < count; first++)
        {
            var assignment = inputs.Select(x => -x).ToArray();
            assignment[first] = inputs[first];
            Assert.Equal(CryptoMiniSatNative.Lbool.True, solver.SolveWithAssumptions(assignment));
            for (int second = first + 1; second < count; second++)
            {
                assignment[second] = inputs[second];
                Assert.Equal(CryptoMiniSatNative.Lbool.False, solver.SolveWithAssumptions(assignment));
                assignment[second] = -inputs[second];
            }
        }
    }
}
