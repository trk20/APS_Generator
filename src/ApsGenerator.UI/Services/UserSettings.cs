using ApsGenerator.Core.Models;
using ApsGenerator.UI.Models;

namespace ApsGenerator.UI.Services;

public sealed class UserSettings
{
    public const string DefaultExportNameTemplate = "APS_{width}x{height}_{clips}clip_x{count}_{targetHeight}h";

    public TemplateShape TemplateShape { get; set; } = TemplateShape.CircleCenterHole;

    public int TemplateWidth { get; set; } = ApsGenerationDefaults.TemplateWidth;

    public int TemplateHeight { get; set; } = ApsGenerationDefaults.TemplateDepth;

    public bool IsHeightLocked { get; set; } = true;

    public TetrisType SelectedTetrisType { get; set; } = ApsGenerationDefaults.DefaultTetrisType;

    public SymmetryType SelectedSymmetryType { get; set; } = ApsGenerationDefaults.DefaultSymmetryType;

    public bool IsHardSymmetry { get; set; } = ApsGenerationDefaults.DefaultSymmetryMode == SymmetryMode.Hard;

    public bool EarlyStopEnabled { get; set; } = ApsGenerationDefaults.EarlyStopEnabled;

    /// <summary>After Tetris, solve cooler snakes for 3/4/5-clip (persisted with solver options).</summary>
    public bool GenerateCoolerSnake { get; set; } = true;

    /// <summary>Show cooler path overlay on the grid (visual only).</summary>
    public bool ShowCoolerOverlay { get; set; } = true;

    public double MaxTimeSeconds { get; set; } = ApsGenerationDefaults.MaxTimeSeconds;

    public bool IsMaximize { get; set; } = true;

    public int TargetPlacementCount { get; set; } = 0;

    public PaintMode PaintMode { get; set; } = PaintMode.Block;

    public string? LastExportFolder { get; set; }

    public int ThreadCount { get; set; } = Math.Max(1, Environment.ProcessorCount - 1);

    public int DefaultExportHeightBasic { get; set; } = ApsGenerationDefaults.BasicComponentHeight;

    public int DefaultExportHeightFiveClip { get; set; } = FiveClipHeight.MinHeight;

    /// <summary>Persisted extra-layer mode for 3/4-clip exports.</summary>
    public ExportExtraLayers ExportExtraLayersBasic { get; set; } = ApsGenerationDefaults.DefaultExtraLayers;

    /// <summary>Persisted extra-layer mode for 5-clip exports (Cooler Snake or Tetris only).</summary>
    public ExportExtraLayers ExportExtraLayersFiveClip { get; set; } = ApsGenerationDefaults.DefaultExtraLayers;

    public string ExportNameTemplate { get; set; } = DefaultExportNameTemplate;

    public int NumSolutions { get; set; } = ApsGenerationDefaults.NumSolutions;

    public double UiScale { get; set; } = 1.0;

    public bool AutoUpdate { get; set; } = true;

    public bool ReceiveExperimentalUpdates { get; set; } = false;

    public bool ShowReleaseNotesAfterUpdate { get; set; } = true;

    public string? PendingReleaseNotesVersion { get; set; }

    public string? PendingReleaseNotesContent { get; set; }

    public string? LastSeenUpdateVersion { get; set; }
}
