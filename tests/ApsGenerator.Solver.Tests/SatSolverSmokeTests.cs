using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver.Tests;

public class SatSolverSmokeTests
{
    [Fact]
    public void SolverCreatesAndDisposes()
    {
        using SatSolver solver = new();
    }

    [Fact]
    public void TrivialSatisfiable()
    {
        using SatSolver solver = new();
        solver.AddVariables(1);
        solver.AddClause([1]);

        CryptoMiniSatNative.Lbool result = solver.Solve();

        Assert.Equal(CryptoMiniSatNative.Lbool.True, result);

        bool?[] model = solver.GetModel();
        Assert.NotEmpty(model);
        Assert.True(model[0]);
    }

    [Fact]
    public void TrivialUnsatisfiable()
    {
        using SatSolver solver = new();
        solver.AddVariables(1);
        solver.AddClause([1]);
        solver.AddClause([-1]);

        CryptoMiniSatNative.Lbool result = solver.Solve();

        Assert.Equal(CryptoMiniSatNative.Lbool.False, result);
    }

    [Fact]
    public void TwoVariableSolution()
    {
        using SatSolver solver = new();
        solver.AddVariables(2);
        solver.AddClause([1, 2]);
        solver.AddClause([-1, -2]);

        CryptoMiniSatNative.Lbool result = solver.Solve();

        Assert.Equal(CryptoMiniSatNative.Lbool.True, result);

        bool?[] model = solver.GetModel();
        Assert.True(model.Length >= 2);

        int trueCount = 0;
        for (int i = 0; i < 2; i++)
        {
            if (model[i] == true)
            {
                trueCount++;
            }
        }

        Assert.Equal(1, trueCount);
    }

    [Fact]
    public void SolveWithAssumptions()
    {
        using SatSolver solver = new();
        solver.AddVariables(2);
        solver.AddClause([1, 2]);

        CryptoMiniSatNative.Lbool result = solver.SolveWithAssumptions([-1]);

        Assert.Equal(CryptoMiniSatNative.Lbool.True, result);

        bool?[] model = solver.GetModel();
        Assert.True(model.Length >= 2);
        Assert.False(model[0]);
        Assert.True(model[1]);
    }
}