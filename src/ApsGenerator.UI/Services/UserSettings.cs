using ApsGenerator.Core.Models;
using ApsGenerator.UI.Models;

namespace ApsGenerator.UI.Services;

public sealed class UserSettings
{
    public const string DefaultExportNameTemplate = "APS_{width}x{height}_{clips}clip_x{count}_{targetHeight}h";

    public TemplateShape TemplateShape { get; set; } = TemplateShape.CircleCenterHole;

    public int TemplateWidth { get; set; } = 15;

    public int TemplateHeight { get; set; } = 15;

    public bool IsHeightLocked { get; set; } = true;

    public TetrisType SelectedTetrisType { get; set; } = TetrisType.ThreeClip;

    public SymmetryType SelectedSymmetryType { get; set; } = SymmetryType.None;

    public bool IsHardSymmetry { get; set; } = true;

    public bool EarlyStopEnabled { get; set; } = true;

    public double MaxTimeSeconds { get; set; } = 30;

    public bool IsMaximize { get; set; } = true;

    public int TargetPlacementCount { get; set; } = 0;

    public PaintMode PaintMode { get; set; } = PaintMode.Block;

    public string? LastExportFolder { get; set; }

    public int ThreadCount { get; set; } = Math.Max(1, Environment.ProcessorCount - 1);

    public int DefaultExportHeightBasic { get; set; } = 2;

    private int defaultExportHeightFiveClip = FiveClipHeight.MinHeight;

    public int DefaultExportHeightFiveClip
    {
        get => defaultExportHeightFiveClip;
        set => defaultExportHeightFiveClip = FiveClipHeight.RoundToMultipleOf3(value);
    }

    public string ExportNameTemplate { get; set; } = DefaultExportNameTemplate;

    public int NumSolutions { get; set; } = 1;

    public double UiScale { get; set; } = 1.0;
}
