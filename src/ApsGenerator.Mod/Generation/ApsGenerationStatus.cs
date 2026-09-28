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
            SolverStatus.Optimal => "Optimal",
            _ => solution.Result.Status.ToString()
        };
    }
}
