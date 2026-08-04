using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver.Cooler;

/// <summary>Shared SAT variable / clause scaffolding for cooler CNF stages.</summary>
internal sealed class SatClauseBuilder(SatSolver solver)
{
    private int nextVar;
    private int clauseCount;

    public SatSolver Solver => solver;
    public int NextVar => nextVar;
    public int ClauseCount => clauseCount;

    public ref int NextVarRef => ref nextVar;
    public ref int ClauseCountRef => ref clauseCount;

    public void Add(params int[] lits) => Add((ReadOnlySpan<int>)lits);

    public void Add(ReadOnlySpan<int> lits)
    {
        solver.AddClause(lits);
        clauseCount++;
    }

    public int NewVar()
    {
        solver.AddVariables(1);
        return ++nextVar;
    }

    public void AddContradiction() =>
        SatCardinality.AddContradiction(solver, ref nextVar, ref clauseCount);

    public int[] NewVars(int count)
    {
        if (count <= 0)
            return [];

        solver.AddVariables(count);
        var vars = new int[count];
        for (int i = 0; i < count; i++)
            vars[i] = ++nextVar;
        return vars;
    }
}
