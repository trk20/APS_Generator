using ApsGenerator.Core;
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
        SolverResult untrimmed = new TetrisSolver().Solve(
            grid,
            request.Problem.Type,
            request.CreateSolverOptions(grid),
            cancellationToken);
        return Complete(request, grid, untrimmed, [untrimmed.Placements], 1, cancellationToken);
    }

    public ApsSolution Next(
        ApsSolution existing,
        ApsGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (existing.Request.Problem != request.Problem)
            throw new InvalidOperationException("Cannot continue a layout sequence for a different Tetris problem.");

        Grid grid = existing.Grid;
        SolverOptions options = request.CreateSolverOptions(grid);
        // A density target only needs another layout that reaches the target; maximizing
        // needs one at least as dense as the layout already shown.
        int minimumClusters = options.TargetClusterCount ?? existing.Untrimmed.ClusterCount;
        SolverResult alternative = new TetrisSolver().SolveAlternative(
            grid, request.Problem.Type, options, existing.Layouts, minimumClusters, cancellationToken);
        if (alternative.Placements.Count == 0)
        {
            return existing with
            {
                Request = request,
                NextLayoutOutcome = alternative.Status == SolverStatus.TimedOut
                    ? ApsNextLayoutOutcome.TimedOut
                    : ApsNextLayoutOutcome.Exhausted
            };
        }

        // Report the original optimality: the alternative matches that density.
        alternative = alternative with { Status = existing.Untrimmed.Status };
        return Complete(request, grid, alternative, [.. existing.Layouts, alternative.Placements],
            existing.LayoutNumber + 1, cancellationToken);
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
        return existing with { Request = request, Cooler = cooler, NextLayoutOutcome = ApsNextLayoutOutcome.None };
    }

    private static ApsSolution Complete(
        ApsGenerationRequest request,
        Grid grid,
        SolverResult untrimmed,
        IReadOnlyList<IReadOnlyList<Placement>> layouts,
        int layoutNumber,
        CancellationToken cancellationToken)
    {
        SolverResult result = Trim(request, grid, untrimmed);
        CoolerSnakeResult? cooler = SolveCooler(request, grid, result, cancellationToken);
        return new ApsSolution(request, grid, result, cooler)
        {
            Untrimmed = untrimmed,
            Layouts = layouts,
            LayoutNumber = layoutNumber
        };
    }

    /// <summary>The solver stops once it reaches a density target, possibly above it; trim to the exact target.</summary>
    private static SolverResult Trim(ApsGenerationRequest request, Grid grid, SolverResult result)
    {
        if (request.Problem.TargetClusters(grid) is not int target || result.Placements.Count <= target)
            return result;
        IReadOnlyList<Placement> trimmed = PlacementTrimmer.Trim(
            result.Placements, grid, request.Problem.Type, request.Problem.Symmetry, target);
        return result with
        {
            Placements = trimmed,
            AllSolutions = [trimmed],
            EmptyCells = PlacementCoverage.EmptyExclusiveCellCount(
                trimmed, ClusterShape.GetShapes(request.Problem.Type), grid.AvailableCellCount),
            Status = SolverStatus.TargetDensityReached
        };
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

internal enum ApsNextLayoutOutcome
{
    /// <summary>The solution came from Generate, a refresh, or a successful Next.</summary>
    None,
    /// <summary>Next got UNSAT at this density.</summary>
    Exhausted,
    /// <summary>Next ran out of time without finding another layout.</summary>
    TimedOut
}

internal sealed record ApsSolution(ApsGenerationRequest Request, Grid Grid, SolverResult Result, CoolerSnakeResult? Cooler)
{
    internal TetrisType Type => Request.Problem.Type;

    /// <summary>The solver's layout before trimming to a density target.</summary>
    internal SolverResult Untrimmed { get; init; } = Result;

    /// <summary>Every untrimmed layout shown so far for this problem.</summary>
    internal IReadOnlyList<IReadOnlyList<Placement>> Layouts { get; init; } = [Result.Placements];

    /// <summary>1-based position of this layout in the sequence.</summary>
    internal int LayoutNumber { get; init; } = 1;

    internal ApsNextLayoutOutcome NextLayoutOutcome { get; init; }

    /// <summary>How long this solution took to solve.</summary>
    internal double SolveSeconds { get; init; }
}
