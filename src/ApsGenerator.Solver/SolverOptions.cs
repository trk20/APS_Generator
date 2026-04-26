using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver;

public sealed record SolverOptions
{
    public int MaxThreads { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);
    public double MaxTimeSeconds { get; init; } = 30;
    public SymmetryType SymmetryType { get; init; } = SymmetryType.None;
    public SymmetryMode SymmetryMode { get; init; } = SymmetryMode.Hard;

    /// <summary>
    /// When true, the solver will use a heuristic to stop early if it finds a solution that is likely optimal
    /// The result will be marked as LikelyOptimal rather than Optimal.
    /// </summary>
    public bool EarlyStopEnabled { get; init; } = true;

    /// <summary>
    /// Optional target cluster count. If set, solver stops once this many clusters
    /// are placed, without trying to optimize further. Null means optimize for maximum.
    /// </summary>
    public int? TargetClusterCount { get; init; }

    /// <summary>
    /// Number of distinct optimal solutions to enumerate via blocking clauses.
    /// The wall-clock budget is shared across all enumerations.
    /// </summary>
    public int NumSolutions { get; init; } = 1;
}
