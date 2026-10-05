using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

internal static class SatAtMostOne
{
    private const int PairwiseThreshold = 16;

    public static void Add(SatSolver solver, IReadOnlyList<int> inputs, ref int nextVariable)
    {
        if (inputs.Count <= PairwiseThreshold)
        {
            for (int first = 0; first < inputs.Count; first++)
                for (int second = first + 1; second < inputs.Count; second++)
                    solver.AddClause([-inputs[first], -inputs[second]]);
            return;
        }

        solver.AddVariables(inputs.Count - 1);
        var outputs = new int[inputs.Count - 1];
        for (int index = 0; index < outputs.Length; index++)
            outputs[index] = ++nextVariable;
        solver.AddClause([-inputs[0], outputs[0]]);
        solver.AddClause([-inputs[inputs.Count - 1], -outputs[outputs.Length - 1]]);
        for (int index = 1; index < inputs.Count - 1; index++)
        {
            solver.AddClause([-inputs[index], outputs[index]]);
            solver.AddClause([-inputs[index], -outputs[index - 1]]);
            solver.AddClause([-outputs[index - 1], outputs[index]]);
        }
    }
}
