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
    internal Grid CreateGrid() => Shape switch
    {
        ApsTemplateShape.CircleCenterHole => TemplateGenerator.Circle(Width, HoleShape, HoleSize),
        ApsTemplateShape.Circle => TemplateGenerator.Circle(Width, blockCenter: false),
        _ => TemplateGenerator.Rectangle(Width, Depth)
    };
}

internal sealed record TetrisProblem(ApsTemplate Template, TetrisType Type, SymmetryType Symmetry, SymmetryMode SymmetryMode);
internal sealed record ApsOutputOptions(int ComponentHeight, ExportExtraLayers Layers);
internal sealed record ApsSolveLimits(int Seconds, int Threads, bool EarlyStop);

internal sealed record ApsGenerationRequest(TetrisProblem Problem, ApsOutputOptions Output, ApsSolveLimits Limits)
{
    internal ApsLayoutGeometry Geometry => new(Problem.Template.Width, Problem.Template.Depth,
        Output.ComponentHeight, Problem.Type, Output.Layers);

    internal SolverOptions CreateSolverOptions() => new()
    {
        MaxThreads = Limits.Threads,
        MaxTimeSeconds = Limits.Seconds,
        SymmetryType = Problem.Symmetry,
        SymmetryMode = Problem.SymmetryMode,
        EarlyStopEnabled = Limits.EarlyStop,
        NumSolutions = 1
    };
}
