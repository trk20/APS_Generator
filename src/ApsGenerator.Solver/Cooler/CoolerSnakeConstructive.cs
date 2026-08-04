using System.Diagnostics;

namespace ApsGenerator.Solver.Cooler;

/// <summary>
/// Constructive snake: prefer vertical open-arm ejectors, place top intakes, Steiner tree.
/// </summary>
internal static class CoolerSnakeConstructive
{
    public static CoolerStageResult? TrySolve(
        CoolerRoutingContext ctx,
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var hit = CoolerRoutingSearch.TryFirstHit(
            ctx,
            EjectorAssignmentSearch.ForConstructive(catalog),
            CoolerSolverConstants.ConstructiveIntakeTrials,
            CoolerSolverConstants.ConstructiveIntakeSeed,
            (routingCtx, packing, assignment, intakeMask) =>
                CoolerGraph.TrySteinerDeckStage(
                    routingCtx, packing, assignment, intakeMask, CoolerStageDetails.Constructive),
            cancellationToken);

        sw.Stop();
        return hit?.WithTiming(sw.Elapsed.TotalSeconds);
    }
}
