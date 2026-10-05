using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver.Interop;

namespace ApsGenerator.Solver;

internal sealed class PackingProblem
{
    private readonly Grid grid;
    private readonly SolverOptions options;
    private readonly Dictionary<(int, int), List<int>> exclusive = [];
    private readonly Dictionary<(int, int), List<int>> connections = [];
    private readonly IReadOnlyList<ClusterShape> shapes;
    private readonly List<(int Row, int Col)> cells = [];

    public List<Placement> Placements { get; }
    public int AvailableCells => grid.AvailableCellCount;
    public int Step { get; }

    public PackingProblem(Grid grid, TetrisType type, SolverOptions options)
    {
        this.grid = grid;
        this.options = options;
        Placements = PlacementEnumerator.Enumerate(grid, type);
        shapes = ClusterShape.GetShapes(type);
        Step = type switch
        {
            TetrisType.ThreeClip or TetrisType.FiveClip => 4,
            TetrisType.FourClip => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        BuildCellMaps();
        for (int row = 0; row < grid.Height; row++)
            for (int col = 0; col < grid.Width; col++)
                if (grid.IsAvailable(row, col))
                    cells.Add((row, col));
    }

    public void Configure(SatSolver solver, ref int nextVariable)
    {
        if (options.MaxThreads > 1)
            solver.SetThreadCount(options.MaxThreads);
        nextVariable = Placements.Count;
        solver.AddVariables(Placements.Count);
        AddPlacementConflicts(solver, ref nextVariable);
        PlacementSymmetry.Add(solver, grid, Placements, shapes, options.SymmetryType, options.SymmetryMode);
    }

    public SymmetricCoverage.BoundEncoding EncodeObjective(SatSolver solver, ref int nextVariable)
    {
        if (options.SymmetryMode == SymmetryMode.Hard && options.SymmetryType != SymmetryType.None)
            return SymmetricCoverage.BuildEncoding(solver, grid, cells, exclusive,
                options.SymmetryType, ref nextVariable);

        solver.AddVariables(cells.Count);
        int firstCovered = nextVariable + 1;
        nextVariable += cells.Count;
        var terms = new WeightedLiteral[cells.Count];
        for (int index = 0; index < cells.Count; index++)
        {
            int covered = firstCovered + index;
            terms[index] = new(-covered, 1);
            var covering = exclusive.TryGetValue(cells[index], out var placements) ? placements : [];
            var clause = new int[covering.Count + 1];
            clause[0] = -covered;
            for (int placement = 0; placement < covering.Count; placement++)
                clause[placement + 1] = covering[placement] + 1;
            solver.AddClause(clause);
        }
        return new(terms, 1, 0);
    }

    public int CountEmptyCells(bool?[] model) => AvailableCells - CountClusters(model) * Step;

    public int CountClusters(bool?[] model)
    {
        int count = 0;
        for (int index = 0; index < Placements.Count; index++)
            if (model[index] == true)
                count++;
        return count;
    }

    public int GetNextBound(int emptyCells)
    {
        int remainder = AvailableCells % Step;
        int delta = (emptyCells % Step - remainder + Step) % Step;
        return emptyCells - delta - Step;
    }

    public SolverResult Decode(bool?[] model, SolverStatus status)
    {
        var selected = Placements.Where((placement, index) => model[index] == true).ToArray();
        return new()
        {
            Placements = selected, AllSolutions = [selected],
            EmptyCells = PlacementCoverage.EmptyExclusiveCellCount(selected, shapes, AvailableCells),
            Status = status
        };
    }

    private void BuildCellMaps()
    {
        for (int index = 0; index < Placements.Count; index++)
        {
            var placement = Placements[index];
            foreach (var offset in shapes[placement.ShapeIndex].Offsets)
            {
                var map = offset.Role == CellRole.Connection ? connections : exclusive;
                var cell = (placement.Row + offset.DeltaRow, placement.Col + offset.DeltaCol);
                if (!map.TryGetValue(cell, out var covering))
                    map[cell] = covering = [];
                covering.Add(index);
            }
        }
    }

    private void AddPlacementConflicts(SatSolver solver, ref int nextVariable)
    {
        foreach (var (cell, covering) in exclusive)
        {
            SatAtMostOne.Add(solver, covering.Select(index => index + 1).ToArray(), ref nextVariable);
            if (!connections.TryGetValue(cell, out var connected))
                continue;
            foreach (int placement in covering)
                foreach (int connection in connected)
                    solver.AddClause([-(placement + 1), -(connection + 1)]);
        }
    }
}
