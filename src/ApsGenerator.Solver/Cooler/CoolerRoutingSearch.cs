namespace ApsGenerator.Solver.Cooler;

/// <summary>
/// Shared assignment x intake-mask trial loops for constructive and bridge stages.
/// </summary>
internal static class CoolerRoutingSearch
{
    public static CoolerStageResult? TryFirstHit(
        CoolerRoutingContext ctx,
        IEnumerable<IReadOnlyList<EjectorCandidate>> assignments,
        int intakeTrials,
        int intakeSeed,
        Func<CoolerRoutingContext, PackingAnalysis, IReadOnlyList<EjectorCandidate>, bool[], CoolerStageResult?> route,
        CancellationToken cancellationToken,
        bool tryMinimizingComponents = false)
    {
        var packing = ctx.Packing;
        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var intakeMask in CoolerIntakeAssign.EnumerateMaskTrials(
                         ctx, assignment, intakeTrials, intakeSeed, tryMinimizingComponents))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stage = route(ctx, packing, assignment, intakeMask);
                if (stage is not null)
                    return stage;
            }
        }

        return null;
    }
}
