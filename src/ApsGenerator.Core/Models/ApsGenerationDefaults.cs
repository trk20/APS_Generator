namespace ApsGenerator.Core.Models;

public static class ApsGenerationDefaults
{
    public const int TemplateWidth = 15;
    public const int TemplateDepth = 15;
    public const int BasicComponentHeight = 2;
    public const int MaxTimeSeconds = 30;
    public const int CenterHoleSize = 1;
    public const int NumSolutions = 1;
    public const bool EarlyStopEnabled = true;
    public const TetrisType DefaultTetrisType = TetrisType.ThreeClip;
    public const SymmetryType DefaultSymmetryType = SymmetryType.None;
    public const SymmetryMode DefaultSymmetryMode = SymmetryMode.Hard;
    public const CenterHoleShape DefaultCenterHoleShape = CenterHoleShape.Circle;
    public const ExportExtraLayers DefaultExtraLayers = ExportExtraLayers.EjectorsIntakesCoolerSnake;
}
