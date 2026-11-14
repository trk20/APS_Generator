using ApsGenerator.Core.Models;
using ApsGenerator.Solver;

namespace ApsGenerator.Mod.Generation;

internal static class ApsGenerationStatus
{
    internal static string Describe(ApsSolution solution)
    {
        if (solution.Result.Placements.Count == 0)
            return solution.Result.Status == SolverStatus.TimedOut ? "Timed out, no layout found" : "No layout found";
        if (solution.Request.Output.Layers.NeedsCoolerSolve(solution.Type) &&
            solution.Cooler is not { Status: CoolerSnakeStatus.Sat })
            return solution.Cooler?.Status == CoolerSnakeStatus.TimedOut
                ? "Partial, cooler timed out"
                : "Partial, cooler unavailable";
        return solution.Result.Status switch
        {
            SolverStatus.TimedOut => "Timed out, best layout found applied",
            SolverStatus.LikelyOptimal => "Likely optimal",
            SolverStatus.TargetDensityReached => "Target density",
            SolverStatus.Optimal => "Optimal",
            _ => solution.Result.Status.ToString()
        };
    }

    /// <summary>Explains why Next kept the current layout, or null when it found a new one.</summary>
    internal static string? DescribeNextLayoutOutcome(ApsSolution solution) => solution.NextLayoutOutcome switch
    {
        ApsNextLayoutOutcome.Exhausted => "No other solution at this density",
        ApsNextLayoutOutcome.TimedOut => "No other solution found in time",
        _ => null
    };
}
