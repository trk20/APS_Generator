using ApsGenerator.Core.Models;

namespace ApsGenerator.Solver.Cooler;

internal sealed record CoolerStageResult
{
    public required bool Satisfiable { get; init; }
    public required bool TimedOut { get; init; }
    public string Detail { get; init; } = "";
    public double SolveSeconds { get; init; }
    public int Layers { get; init; } = 1;
    public IReadOnlyList<EjectorPlacement> Ejectors { get; init; } = [];
    public IReadOnlyList<CellKey> TopIntakes { get; init; } = [];
    public IReadOnlyList<CellKey> CoolerCells { get; init; } = [];
    /// <summary>Elevated coolers used for bridges.</summary>
    public IReadOnlyList<CellKey> BridgeCoolerCells { get; init; } = [];
    public IReadOnlyDictionary<CellKey, CoolerFaceFlags> OpenFaces { get; init; }
        = new Dictionary<CellKey, CoolerFaceFlags>();
    public IReadOnlyDictionary<CellKey, CoolerFaceFlags> BridgeOpenFaces { get; init; }
        = new Dictionary<CellKey, CoolerFaceFlags>();

    public CoolerStageResult WithTiming(double seconds) => this with { SolveSeconds = seconds };

    public static CoolerStageResult FromDeckRouting(
        string detail,
        double solveSeconds,
        IReadOnlyList<EjectorCandidate> assignment,
        IReadOnlyList<CellKey> cells,
        IReadOnlyList<CellKey> topIntakes,
        HashSet<int> deckTree,
        Dictionary<int, CoolerFaceFlags> openDeck,
        HashSet<int>? bridgeTree = null,
        Dictionary<int, CoolerFaceFlags>? bridgeOpen = null,
        int layers = 1)
    {
        bridgeTree ??= [];
        bridgeOpen ??= [];

        return new CoolerStageResult
        {
            Satisfiable = true,
            TimedOut = false,
            Detail = detail,
            SolveSeconds = solveSeconds,
            Layers = layers,
            Ejectors = assignment.Select(a => a.ToPlacement()).ToList(),
            TopIntakes = topIntakes,
            CoolerCells = deckTree.OrderBy(i => i).Select(i => cells[i]).ToList(),
            BridgeCoolerCells = bridgeTree.Count == 0
                ? []
                : bridgeTree.OrderBy(i => i).Select(i => cells[i]).ToList(),
            OpenFaces = openDeck.ToDictionary(kv => cells[kv.Key], kv => kv.Value),
            BridgeOpenFaces = bridgeOpen.Count == 0
                ? []
                : bridgeOpen.ToDictionary(kv => cells[kv.Key], kv => kv.Value),
        };
    }

    public static CoolerSnakeResult ToCoolerSnakeResult(
        CoolerStageResult enc,
        PackingAnalysis packing,
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog,
        double seconds,
        string detail)
    {
        var coolers = BuildCoolerCells(enc, packing);
        int layers = Math.Max(1, enc.Layers);
        if (enc.BridgeCoolerCells.Count > 0)
            layers = Math.Max(layers, 2);

        var (intakes, perCluster) = BuildIntakesAndQuotas(enc, packing, catalog);

        return new CoolerSnakeResult
        {
            Status = CoolerSnakeStatus.Sat,
            LayersUsed = layers,
            CoolerCells = coolers,
            IntakeCells = intakes,
            EjectorDirs = [.. enc.Ejectors.Where(e => e.Kind != EjectorKind.None)],
            RequiredIntakesPerCluster = EjectorCatalog.RequiredTotalIntakes(packing.TetrisType),
            IntakesPerCluster = perCluster,
            SolveSeconds = seconds,
            Detail = detail,
        };
    }

    private static List<CoolerCell> BuildCoolerCells(CoolerStageResult enc, PackingAnalysis packing)
    {
        var exclusive = packing.ExclusiveCells.ToHashSet();
        var bridgeKeys = enc.BridgeCoolerCells
            .Select(c => (c.Row, c.Col))
            .ToHashSet();

        return
        [
            .. enc.CoolerCells.Select(c => MakeCoolerCell(
                c,
                layer: 0,
                isGap: !exclusive.Contains(c),
                enc.OpenFaces.TryGetValue(c, out var deckFaces) ? deckFaces : CoolerFaceFlags.None,
                connectUp: bridgeKeys.Contains((c.Row, c.Col)))),
            .. enc.BridgeCoolerCells.Select(c => MakeCoolerCell(
                c,
                layer: 1,
                isGap: !exclusive.Contains(c),
                enc.BridgeOpenFaces.TryGetValue(c, out var bridgeFaces) ? bridgeFaces : CoolerFaceFlags.None,
                connectDown: true)),
        ];
    }

    internal static CoolerCell MakeCoolerCell(
        CellKey cell,
        int layer,
        bool isGap,
        CoolerFaceFlags openFaces,
        bool connectUp = false,
        bool connectDown = false,
        int snakeId = 0) =>
        new(cell.Row, cell.Col, snakeId, Layer: layer, isGap, openFaces, connectUp, connectDown);

    private static (List<IntakeCell> Intakes, List<int> PerCluster) BuildIntakesAndQuotas(
        CoolerStageResult enc,
        PackingAnalysis packing,
        IReadOnlyList<IReadOnlyList<EjectorCandidate>> catalog)
    {
        var byCluster = enc.Ejectors.ToDictionary(e => e.ClusterIndex);
        var footprintSets = packing.Clusters
            .ToDictionary(c => c.Index, c => c.Footprint.ToHashSet());
        var topsByCluster = enc.TopIntakes
            .SelectMany(top => footprintSets
                .Where(kv => kv.Value.Contains(top))
                .Take(1)
                .Select(kv => (Cluster: kv.Key, Top: top)))
            .GroupBy(x => x.Cluster, x => x.Top)
            .ToDictionary(g => g.Key, g => g.ToList());

        var intakes = new List<IntakeCell>();
        var perCluster = new List<int>();

        foreach (var cluster in packing.Clusters.OrderBy(c => c.Index))
        {
            var ej = byCluster[cluster.Index];
            var candidate = catalog[cluster.Index]
                .First(c => c.Kind == ej.Kind && c.Protrusion.Equals(new CellKey(ej.ProtrudeRow, ej.ProtrudeCol)));

            var clusterIntakes = candidate.BottomIntakeCells
                .Select(cell => new IntakeCell(cell.Row, cell.Col, Layer: -1, IsUnderneath: true))
                .Concat(
                    (topsByCluster.GetValueOrDefault(cluster.Index) ?? [])
                    .Select(top => new IntakeCell(top.Row, top.Col, Layer: 0, IsUnderneath: false)))
                .ToList();

            intakes.AddRange(clusterIntakes);
            perCluster.Add(clusterIntakes.Count);
        }

        return (intakes, perCluster);
    }
}
