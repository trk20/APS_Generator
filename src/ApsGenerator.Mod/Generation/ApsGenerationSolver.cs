using ApsGenerator.Core.Models;
using ApsGenerator.Solver;
using ApsGenerator.Solver.Cooler;

namespace ApsGenerator.Mod.Generation;

internal sealed class ApsGenerationSolver : IApsGenerationSolver
{
    public ApsSolution Solve(
        ApsGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        Grid grid = request.Problem.Template.CreateGrid();
        SolverOptions options = request.CreateSolverOptions();

        SolverResult result = new TetrisSolver().Solve(
            grid,
            request.Problem.Type,
            options,
            cancellationToken);
        CoolerSnakeResult? cooler = SolveCooler(request, grid, result, cancellationToken);
        return new ApsSolution(request, grid, result, cooler);
    }

    public ApsSolution Reconfigure(
        ApsSolution existing,
        ApsGenerationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (existing.Request.Problem != request.Problem)
            throw new InvalidOperationException("Cannot reuse placements for a different Tetris problem.");

        CoolerSnakeResult? cooler = CanReuseCooler(existing, request)
            ? existing.Cooler
            : SolveCooler(request, existing.Grid, existing.Result, cancellationToken);
        return new ApsSolution(request, existing.Grid, existing.Result, cooler);
    }

    private static bool CanReuseCooler(ApsSolution existing, ApsGenerationRequest request)
    {
        if (!request.Output.Layers.NeedsCoolerSolve(request.Problem.Type))
            return existing.Cooler is null;
        if (existing.Cooler is not { Status: CoolerSnakeStatus.Sat } ||
            !existing.Request.Output.Layers.NeedsCoolerSolve(existing.Type))
            return false;
        if (request.Problem.Type == TetrisType.FiveClip)
            return true;

        return existing.Request.Output.Layers.OmitEjectorsForCoolerSolve() ==
               request.Output.Layers.OmitEjectorsForCoolerSolve();
    }

    private static CoolerSnakeResult? SolveCooler(
        ApsGenerationRequest request,
        Grid grid,
        SolverResult result,
        CancellationToken cancellationToken)
    {
        if (!request.Output.Layers.NeedsCoolerSolve(request.Problem.Type) ||
            result.Placements.Count == 0)
            return null;

        return new CoolerSnakeSolver().Solve(
            grid,
            request.Problem.Type,
            result.Placements,
            new CoolerSnakeOptions
            {
                MaxTimeSeconds = request.Limits.Seconds,
                Threads = request.Limits.Threads,
                OmitEjectors = request.Output.Layers.OmitEjectorsForCoolerSolve()
            },
            cancellationToken);
    }
}

internal sealed record ApsSolution(ApsGenerationRequest Request, Grid Grid, SolverResult Result, CoolerSnakeResult? Cooler)
{
    internal TetrisType Type => Request.Problem.Type;
}
