namespace ApsGenerator.Core.Models;

public static class ApsGenerationDefaults
{
    public const int TemplateWidth = 15;
    public const int TemplateDepth = 15;
    public const int BasicComponentHeight = 2;
    public const int MaxTimeSeconds = 15;
    public const int CenterHoleSize = 1;
    public const int NumSolutions = 1;
    public const bool EarlyStopEnabled = false;
    public const TetrisType DefaultTetrisType = TetrisType.ThreeClip;
    public const SymmetryType DefaultSymmetryType = SymmetryType.VerticalReflection;
    public const SymmetryMode DefaultSymmetryMode = SymmetryMode.Hard;
    public const CenterHoleShape DefaultCenterHoleShape = CenterHoleShape.Circle;
    public const ExportExtraLayers DefaultExtraLayers = ExportExtraLayers.EjectorsIntakesCoolerSnake;

    /// <summary>Default share of the available solver threads, leaving a bit of headroom.</summary>
    public const double ThreadShare = 0.75;

    /// <summary>The most solver threads allowed, one less than processor count.</summary>
    public static int MaximumThreadCount => Math.Max(1, Environment.ProcessorCount - 1);

    public static int DefaultThreadCount => DefaultThreadCountFor(MaximumThreadCount);

    public static int DefaultThreadCountFor(int maximumThreads) =>
        Math.Max(1, (int)Math.Floor(maximumThreads * ThreadShare));

    /// <summary>Defaults shipped before v2.5; saved values on old schema version equal to these are migrated to the current defaults.</summary>
    public static class Legacy
    {
        public const int MaxTimeSeconds = 30;
        public const bool EarlyStopEnabled = true;
        public const SymmetryType DefaultSymmetryType = SymmetryType.None;
    }
}
