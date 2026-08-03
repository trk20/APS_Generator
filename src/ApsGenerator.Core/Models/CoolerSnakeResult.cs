namespace ApsGenerator.Core.Models;

/// <param name="SnakeId">Sector index (0 for a single connected Available region).</param>
/// <param name="Layer">0 = top of tetris.</param>
/// <param name="IsGap">True when the cooler routes through an empty template cell.</param>
/// <param name="OpenFaces">N,E,S,W open for cooler–cooler links (and loader attach faces).</param>
public readonly record struct CoolerCell(
    int Row,
    int Col,
    int SnakeId,
    int Layer,
    bool IsGap,
    CoolerFaceFlags OpenFaces,
    bool ConnectUp = false,
    bool ConnectDown = false);

/// <param name="IsUnderneath">True for bottom-layer intakes (Y=−1).</param>
public readonly record struct IntakeCell(int Row, int Col, int Layer, bool IsUnderneath);

/// <summary>
/// Ejector occupies two cells: loader anchor + protrusion.
/// For <see cref="EjectorKind.Bottom"/>, protrusion is the cleared clip at Y=−1.
/// For <see cref="EjectorKind.VerticalOpenArmDown"/>, protrusion is the empty open-arm XY cell.
/// </summary>
public readonly record struct EjectorPlacement(
    int ClusterIndex,
    EjectorKind Kind,
    int LoaderRow,
    int LoaderCol,
    int ProtrudeRow,
    int ProtrudeCol,
    int TopDeficit)
{
    public int DCol => ProtrudeCol - LoaderCol;
    public int DRow => ProtrudeRow - LoaderRow;
}

public sealed record CoolerSnakeResult
{
    public required CoolerSnakeStatus Status { get; init; }
    public int LayersUsed { get; init; } = 1;
    public IReadOnlyList<CoolerCell> CoolerCells { get; init; } = [];
    public IReadOnlyList<IntakeCell> IntakeCells { get; init; } = [];
    public IReadOnlyList<EjectorPlacement> EjectorDirs { get; init; } = [];
    public int RequiredIntakesPerCluster { get; init; }
    public IReadOnlyList<int> IntakesPerCluster { get; init; } = [];
    public double SolveSeconds { get; init; }
    public string Detail { get; init; } = "";

    public bool IntakesMeetQuota =>
        RequiredIntakesPerCluster <= 0
        || (IntakesPerCluster.Count > 0
            && IntakesPerCluster.All(n => n >= RequiredIntakesPerCluster));
}
