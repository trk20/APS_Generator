using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver;

public sealed record SolverResult
{
    public required IReadOnlyList<Placement> Placements { get; init; }
    public required IReadOnlyList<IReadOnlyList<Placement>> AllSolutions { get; init; }
    public int ClusterCount => Placements.Count;
    public required int EmptyCells { get; init; }
    public required SolverStatus Status { get; init; }
}
