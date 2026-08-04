using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

/// <summary>5-clip: Steiner tree linking connection-shaft tops across Available sectors.</summary>
internal static class FiveClipSnake
{
    public static CoolerSnakeResult Solve(
        Grid grid,
        IReadOnlyList<Placement> placements,
        CancellationToken cancellationToken = default)
    {
        if (placements.Count == 0)
            return SatResult([], CoolerStageDetails.FiveClip);

        var (connections, exclusive) = CollectConnections(placements);
        if (connections.Count == 0)
            return SatResult([], CoolerStageDetails.FiveClip);

        var occupied = CoolerSectors.AvailableComponents(grid)
            .Select(comp => (
                Cells: comp,
                Shafts: connections.Where(comp.Contains).ToHashSet()))
            .Where(s => s.Shafts.Count > 0)
            .ToList();

        if (occupied.Count == 0)
        {
            return new CoolerSnakeResult
            {
                Status = CoolerSnakeStatus.Unsat,
                Detail = CoolerStageDetails.FiveClipDisconnected,
            };
        }

        var parts = new List<(IReadOnlyList<CoolerCell> Cells, int SnakeId)>(occupied.Count);
        var details = new List<string>(occupied.Count);

        for (int snakeId = 0; snakeId < occupied.Count; snakeId++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (sectorCells, shafts) = occupied[snakeId];
            if (!TrySolveSector(sectorCells, shafts, exclusive, out var coolerCells, out var detail))
            {
                return new CoolerSnakeResult
                {
                    Status = CoolerSnakeStatus.Unsat,
                    Detail = occupied.Count == 1
                        ? CoolerStageDetails.FiveClipDisconnected
                        : $"sector {snakeId}: {CoolerStageDetails.FiveClipDisconnected}",
                };
            }

            parts.Add((coolerCells, snakeId));
            details.Add(detail);
        }

        string mergedDetail = parts.Count == 1
            ? details[0]
            : $"{parts.Count} sectors: {string.Join("; ", details)}";

        return SatResult(CoolerSectors.WithSnakeIds(parts), mergedDetail);
    }

    private static bool TrySolveSector(
        HashSet<CellKey> sectorCells,
        HashSet<CellKey> shafts,
        HashSet<CellKey> exclusive,
        out List<CoolerCell> coolerCells,
        out string detail)
    {
        var preferred = sectorCells
            .Where(c => !exclusive.Contains(c) || shafts.Contains(c))
            .OrderBy(c => c.Row)
            .ThenBy(c => c.Col)
            .ToList();
        if (TryBuildSnake(preferred, shafts, exclusive, out coolerCells, out detail))
            return true;

        var fallback = sectorCells
            .OrderBy(c => c.Row)
            .ThenBy(c => c.Col)
            .ToList();
        if (TryBuildSnake(fallback, shafts, exclusive, out coolerCells, out detail))
        {
            detail = CoolerStageDetails.FiveClipWithFootprint;
            return true;
        }

        coolerCells = [];
        detail = CoolerStageDetails.FiveClipDisconnected;
        return false;
    }

    private static CoolerSnakeResult SatResult(IReadOnlyList<CoolerCell> coolerCells, string detail) => new()
    {
        Status = CoolerSnakeStatus.Sat,
        CoolerCells = coolerCells,
        Detail = detail,
        RequiredIntakesPerCluster = 0,
    };

    private static (HashSet<CellKey> Connections, HashSet<CellKey> Exclusive) CollectConnections(
        IReadOnlyList<Placement> placements)
    {
        var shapes = ClusterShape.GetShapes(TetrisType.FiveClip);
        var cells = placements.SelectMany(p =>
        {
            var shape = shapes[p.ShapeIndex];
            return shape.Offsets.Select(o => (
                Cell: new CellKey(p.Row + o.DeltaRow, p.Col + o.DeltaCol),
                o.Role));
        }).ToList();

        return (
            cells.Where(x => x.Role == CellRole.Connection).Select(x => x.Cell).ToHashSet(),
            cells.Select(x => x.Cell).ToHashSet());
    }

    private static bool TryBuildSnake(
        IReadOnlyList<CellKey> cells,
        HashSet<CellKey> connections,
        HashSet<CellKey> exclusive,
        out List<CoolerCell> coolerCells,
        out string detail)
    {
        coolerCells = [];
        detail = CoolerStageDetails.FiveClip;
        int n = cells.Count;
        if (n == 0)
            return false;

        var indexOf = cells.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        var neighborsDir = CoolerGraph.BuildNeighborsDir(cells, indexOf);
        var neighbors = CoolerGraph.UndirectedFromDir(neighborsDir);
        var terminalIdx = connections
            .Where(indexOf.ContainsKey)
            .Select(c => indexOf[c])
            .OrderBy(i => i)
            .ToList();
        if (terminalIdx.Count == 0)
            return false;

        var active = Enumerable.Range(0, n).ToHashSet();
        if (!CoolerGraph.TrySteinerOpen(active, neighbors, neighborsDir, terminalIdx, out var steiner, out var open))
            return false;

        coolerCells = [.. steiner
            .Select(i =>
            {
                var cell = cells[i];
                bool onShaft = connections.Contains(cell);
                return CoolerStageResult.MakeCoolerCell(
                    cell,
                    layer: 0,
                    isGap: !exclusive.Contains(cell),
                    open.TryGetValue(i, out var f) ? f : CoolerFaceFlags.None,
                    connectUp: false,
                    connectDown: onShaft);
            })];
        return true;
    }
}
