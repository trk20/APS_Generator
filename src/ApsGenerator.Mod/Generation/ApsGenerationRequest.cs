using System.Collections.Concurrent;
using ApsGenerator.Core;
using ApsGenerator.Core.Export;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver;

namespace ApsGenerator.Mod.Generation;

internal enum ApsSettingsChangeImpact { None, TetrisSolve, Prefab, FutureSolve }

internal sealed record ApsTemplate(
    ApsTemplateShape Shape,
    int Width,
    int Depth,
    CenterHoleShape HoleShape,
    int HoleSize)
{
    private static readonly ConcurrentDictionary<ApsTemplate, int> CellCounts = new();

    internal Grid CreateGrid() => Shape switch
    {
        ApsTemplateShape.CircleCenterHole => TemplateGenerator.Circle(Width, HoleShape, HoleSize),
        ApsTemplateShape.Circle => TemplateGenerator.Circle(Width, blockCenter: false),
        _ => TemplateGenerator.Rectangle(Width, Depth)
    };

    /// <summary>Cache for available cells</summary>
    internal int AvailableCells => CellCounts.GetOrAdd(this, template => template.CreateGrid().AvailableCellCount);
}

internal sealed record TetrisProblem(
    ApsTemplate Template,
    TetrisType Type,
    SymmetryType Symmetry,
    SymmetryMode SymmetryMode,
    int TargetDensityPercent = 100)
{
    /// <summary>Cluster target for the solver (null to maximize).</summary>
    internal int? TargetClusters(Grid grid)
    {
        if (TargetDensityPercent >= 100)
            return null;
        int maximum = Type.TheoreticalMaxClusters(grid.AvailableCellCount);
        return Math.Max(1, (int)Math.Round(maximum * TargetDensityPercent / 100.0));
    }
}
internal sealed record ApsOutputOptions(int ComponentHeight, ExportExtraLayers Layers);
internal sealed record ApsSolveLimits(int Seconds, int Threads, bool EarlyStop);

internal sealed record ApsGenerationRequest(TetrisProblem Problem, ApsOutputOptions Output, ApsSolveLimits Limits)
{
    internal ApsLayoutGeometry Geometry => new(Problem.Template.Width, Problem.Template.Depth,
        Output.ComponentHeight, Problem.Type, Output.Layers);

    internal SolverOptions CreateSolverOptions(Grid grid) => new()
    {
        MaxThreads = Limits.Threads,
        MaxTimeSeconds = Limits.Seconds,
        SymmetryType = Problem.Symmetry,
        SymmetryMode = Problem.SymmetryMode,
        EarlyStopEnabled = Limits.EarlyStop,
        TargetClusterCount = Problem.TargetClusters(grid),
        NumSolutions = 1
    };
}
