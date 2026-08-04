using System.Diagnostics;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver.Cooler;

/// <summary>
/// Fast path: fix ejectors, SAT only top intakes + undirected reachability,
/// then Steiner tree and open faces.
/// </summary>
internal static class CoolerSnakeUndirected
{
    public static CoolerStageResult TrySolve(
        CoolerRoutingContext ctx,
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        CoolerSnakeOptions options,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        double budget = Math.Max(CoolerSolverConstants.MinRemainingBudgetSeconds, options.MaxTimeSeconds);
        var packing = ctx.Packing;

        if (ctx.ChainLoaderIdx.Count == 0)
        {
            foreach (var assignment in EjectorAssignmentSearch.ForUndirected(
                         catalog, options.UndirectedRandomTrials))
            {
                sw.Stop();
                return new CoolerStageResult
                {
                    Satisfiable = true,
                    TimedOut = false,
                    Detail = CoolerStageDetails.UndirectedEmpty,
                    SolveSeconds = sw.Elapsed.TotalSeconds,
                    Ejectors = assignment.Select(a => a.ToPlacement()).ToList(),
                };
            }

            sw.Stop();
            return Unsat(sw);
        }

        using var solver = new SatSolver();
        var builder = new SatClauseBuilder(solver);
        if (options.Threads > 1)
            solver.SetThreadCount(options.Threads);

        int[] isIntake = CoolerIntakeCnf.AllocateIntakeVars(builder, ctx);
        EncodeUndirectedReachability(builder, ctx, isIntake, ctx.CellCount);
        var selectors = CoolerIntakeCnf.EncodeCatalogSelectors(builder, ctx, isIntake, catalog);

        int threads = Math.Max(1, options.Threads);

        foreach (var assignment in EjectorAssignmentSearch.ForUndirected(
                     catalog, options.UndirectedRandomTrials))
        {
            cancellationToken.ThrowIfCancellationRequested();
            double rem = budget - sw.Elapsed.TotalSeconds;
            if (rem <= CoolerSolverConstants.MinRemainingBudgetSeconds)
            {
                sw.Stop();
                return new CoolerStageResult
                {
                    Satisfiable = false,
                    TimedOut = true,
                    Detail = CoolerStageDetails.UndirectedTimeout,
                    SolveSeconds = sw.Elapsed.TotalSeconds,
                };
            }

            if (!TryBuildAssumptions(assignment, selectors, out var assumptions))
                continue;

            solver.SetMaxTime(rem * threads);
            CryptoMiniSatNative.Lbool result;
            using (cancellationToken.Register(solver.Interrupt))
                result = solver.SolveWithAssumptions(assumptions);

            if (result == CryptoMiniSatNative.Lbool.Undef)
                continue;
            if (result == CryptoMiniSatNative.Lbool.False)
                continue;

            var hit = DecodeAndSteiner(solver, packing, ctx, assignment, isIntake);
            if (hit is null)
                continue;

            sw.Stop();
            return hit.WithTiming(sw.Elapsed.TotalSeconds);
        }

        sw.Stop();
        return Unsat(sw);
    }

    private static CoolerStageResult Unsat(Stopwatch sw) => new()
    {
        Satisfiable = false,
        TimedOut = false,
        Detail = CoolerStageDetails.UndirectedUnsat,
        SolveSeconds = sw.Elapsed.TotalSeconds,
    };

    private static bool TryBuildAssumptions(
        IReadOnlyList<EjectorCandidate> assignment,
        Dictionary<(int ClusterIndex, int TopDeficit), int> selectors,
        out int[] assumptions)
    {
        assumptions = new int[assignment.Count];
        for (int i = 0; i < assignment.Count; i++)
        {
            var candidate = assignment[i];
            if (!selectors.TryGetValue((candidate.ClusterIndex, candidate.TopDeficit), out int sel))
            {
                assumptions = [];
                return false;
            }

            assumptions[i] = sel;
        }

        return true;
    }

    private static void EncodeUndirectedReachability(
        SatClauseBuilder builder,
        CoolerRoutingContext ctx,
        int[] isIntake,
        int n)
    {
        int root = ctx.ChainLoaderIdx[0][0];
        builder.Add(-isIntake[root]);

        int maxD = CoolerSolverConstants.ReachabilityMaxDepth(n);
        var reach = CoolerReachability.AllocateDepthVars(builder, n, maxD);

        for (int i = 0; i < n; i++)
        for (int d = 0; d <= maxD; d++)
            builder.Add(-reach[i][d], -isIntake[i]);

        CoolerReachability.FixSingleRoot(builder, reach, root);
        CoolerReachability.EncodeUndirectedSteps(builder, reach, ctx.Neighbors, maxD);

        foreach (var chain in ctx.ChainLoaderIdx)
        {
            var attach = CoolerGraph.AttachmentSet(chain, ctx.Neighbors);
            CoolerReachability.EncodeAttachmentReached(builder, reach, attach, maxD);
        }
    }

    private static CoolerStageResult? DecodeAndSteiner(
        SatSolver solver,
        PackingAnalysis packing,
        CoolerRoutingContext ctx,
        IReadOnlyList<EjectorCandidate> assignment,
        int[] isIntake)
    {
        var model = solver.GetModel();
        bool Lit(int lit) => model[lit - 1] == true;

        int n = ctx.CellCount;
        var intakeMask = new bool[n];
        var intakes = new List<CellKey>();
        for (int i = 0; i < n; i++)
        {
            if (!Lit(isIntake[i]))
                continue;
            intakeMask[i] = true;
            intakes.Add(ctx.Cells[i]);
        }

        return CoolerGraph.TrySteinerDeckStage(
            ctx, packing, assignment, intakeMask, CoolerStageDetails.Undirected, topIntakes: intakes);
    }
}
