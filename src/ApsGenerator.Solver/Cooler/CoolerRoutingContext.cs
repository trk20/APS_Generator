namespace ApsGenerator.Solver.Cooler;

internal sealed class CoolerRoutingContext
{
    public PackingAnalysis Packing { get; }
    public IReadOnlyList<CellKey> Cells { get; }
    public Dictionary<CellKey, int> IndexOf { get; }
    public HashSet<CellKey> ExclusiveSet { get; }
    public List<int>[] Neighbors { get; }
    public List<(int Nb, int Dir)>[] NeighborsDir { get; }
    public List<List<int>> ChainLoaderIdx { get; }
    public int CellCount => Cells.Count;

    private CoolerRoutingContext(
        PackingAnalysis packing,
        IReadOnlyList<CellKey> cells,
        Dictionary<CellKey, int> indexOf,
        HashSet<CellKey> exclusiveSet,
        List<int>[] neighbors,
        List<(int Nb, int Dir)>[] neighborsDir,
        List<List<int>> chainLoaderIdx)
    {
        Packing = packing;
        Cells = cells;
        IndexOf = indexOf;
        ExclusiveSet = exclusiveSet;
        Neighbors = neighbors;
        NeighborsDir = neighborsDir;
        ChainLoaderIdx = chainLoaderIdx;
    }

    public static CoolerRoutingContext From(PackingAnalysis packing)
    {
        var cells = packing.FootprintCells;
        var indexOf = cells.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);

        var exclusiveSet = packing.ExclusiveCells.ToHashSet();
        var neighborsDir = CoolerGraph.BuildNeighborsDir(cells, indexOf);
        var neighbors = CoolerGraph.UndirectedFromDir(neighborsDir);
        var chainLoaderIdx = packing.LoaderChains
            .Select(ch => ch.Select(c => indexOf[c]).ToList())
            .ToList();

        return new CoolerRoutingContext(
            packing, cells, indexOf, exclusiveSet, neighbors, neighborsDir, chainLoaderIdx);
    }

    public HashSet<int> ActiveWithoutIntakes(bool[] intakeMask) =>
        CoolerGraph.IndicesWithoutMask(Cells.Count, intakeMask);
}
