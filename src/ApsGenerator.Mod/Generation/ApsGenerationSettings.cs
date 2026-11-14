using ApsGenerator.Core.Models;

namespace ApsGenerator.Mod.Generation;

internal enum ApsTemplateShape
{
    CircleCenterHole,
    Circle,
    Rectangle
}

internal sealed class ApsGenerationSettings
{
    private int requestedHoleSize;

    internal const int MinimumDimension = 3;
    internal const int MinimumCenterHoleDimension = 5;
    internal const int MaximumDimension = 49;
    internal const int MinimumComponentLength = 1;
    internal const int MaximumComponentLength = 8;
    internal const int MinimumFiveClipHeight = 3;
    internal const int MaximumFiveClipHeight = 24;
    internal const int FiveClipSectionHeight = ApsGenerator.Core.Export.ApsLayoutGeometry.FiveClipSectionHeight;
    internal const int MinimumSeconds = 1;
    internal const int MaximumSeconds = 120;
    internal const int MinimumDensityPercent = 35;
    internal const int MaximumDensityPercent = 100;
    internal const int DensityPercentStep = 5;
    internal const int MinimumHudScalePercent = 50;
    internal const int MaximumHudScalePercent = 160;
    internal const int HudScalePercentStep = 10;

    internal ApsGenerationSettings() => ResetToDefaults();

    internal ApsTemplateShape TemplateShape { get; private set; }
    internal int Width { get; private set; }
    internal int Depth { get; private set; }
    internal int ComponentLength { get; private set; }
    internal CenterHoleShape HoleShape { get; private set; }
    internal int HoleSize { get; private set; }
    internal bool SnapToOddDimensions { get; private set; }
    internal bool AllowEvenDimensions => !SnapToOddDimensions;
    internal TetrisType TetrisType { get; private set; }
    internal SymmetryType SymmetryType { get; private set; }
    internal SymmetryMode SymmetryMode { get; private set; }
    internal int MaxTimeSeconds { get; private set; }
    internal int MaxThreads { get; private set; }
    internal bool EarlyStopEnabled { get; private set; }
    internal ExportExtraLayers ExtraLayers { get; private set; }
    /// <summary>Target cluster count as a percentage of the theoretical maximum; 100 maximizes.</summary>
    internal int TargetDensityPercent { get; private set; }
    /// <summary>User multiplier on the resolution-based HUD scale.</summary>
    internal int HudScalePercent { get; private set; } = 100;

    internal bool IsDepthLocked => TemplateShape != ApsTemplateShape.Rectangle;
    internal int MaximumThreads => ApsGenerationDefaults.MaximumThreadCount;
    internal int DefaultThreads => ApsGenerationDefaults.DefaultThreadCountFor(MaximumThreads);
    internal bool IsMaximizingDensity => TargetDensityPercent >= MaximumDensityPercent;

    /// <summary>How the current template compares with the size limits for the current Tetris options.</summary>
    internal ApsSolveSize SolveSize => ApsSolveSizeLimits.Classify(
        CreateRequest().Problem.Template.AvailableCells, TetrisType, SymmetryType, SymmetryMode);
    internal bool IsUnusuallyLarge => SolveSize != ApsSolveSize.Normal;
    internal bool IsTooLarge => SolveSize == ApsSolveSize.TooLarge;

    /// <summary>Largest width the current shape allows with <paramref name="symmetry"/> (and the current mode).</summary>
    internal int MaximumWidthFor(SymmetryType symmetry) =>
        WidthLimitFor(symmetry, SymmetryMode, ApsSolveSizeLimits.Limit.Maximum);

    /// <summary>Largest width before the large-solve warning with <paramref name="symmetry"/>.</summary>
    internal int RecommendedWidthFor(SymmetryType symmetry) =>
        WidthLimitFor(symmetry, SymmetryMode, ApsSolveSizeLimits.Limit.Recommended);

    /// <summary>Largest width of the current shape and depth within <paramref name="limit"/>.</summary>
    internal int WidthLimitFor(SymmetryType symmetry, SymmetryMode mode, ApsSolveSizeLimits.Limit limit)
    {
        int cells = ApsSolveSizeLimits.CellBudget(TetrisType, symmetry, mode, limit);
        int largest = MinimumTemplateDimension;
        for (int width = MinimumTemplateDimension; width <= MaximumDimension; width++)
        {
            var template = new ApsTemplate(TemplateShape, width, IsDepthLocked ? width : Depth, HoleShape,
                NormalizeHoleSizeFor(width, requestedHoleSize));
            if (template.AvailableCells > cells)
                break;
            largest = width;
        }
        return largest;
    }

    internal void ResetToDefaults()
    {
        int hudScale = HudScalePercent;
        Apply(new ApsSettingsData());
        HudScalePercent = hudScale;
    }

    internal ApsSettingsChangeImpact Change(Action change)
    {
        ApsGenerationRequest previous = CreateRequest();
        (bool, int) previousPreferences = (SnapToOddDimensions, HudScalePercent);
        change();
        ApsGenerationRequest current = CreateRequest();
        if (previous == current)
            return previousPreferences != (SnapToOddDimensions, HudScalePercent)
                ? ApsSettingsChangeImpact.FutureSolve
                : ApsSettingsChangeImpact.None;
        if (previous.Problem != current.Problem) return ApsSettingsChangeImpact.TetrisSolve;
        return previous.Output != current.Output ? ApsSettingsChangeImpact.Prefab : ApsSettingsChangeImpact.FutureSolve;
    }

    internal ApsSettingsData Capture() => new()
    {
        SchemaVersion = ApsSettingsData.CurrentSchemaVersion,
        TemplateShape = TemplateShape,
        Width = Width,
        Depth = Depth,
        ComponentLength = ComponentLength,
        HoleShape = HoleShape,
        HoleSize = requestedHoleSize,
        SnapToOddDimensions = SnapToOddDimensions,
        TetrisType = TetrisType,
        SymmetryType = SymmetryType,
        SymmetryMode = SymmetryMode,
        MaxTimeSeconds = MaxTimeSeconds,
        MaxThreads = MaxThreads == DefaultThreads ? 0 : MaxThreads,
        EarlyStopEnabled = EarlyStopEnabled,
        ExtraLayers = ExtraLayers,
        TargetDensityPercent = TargetDensityPercent,
        HudScalePercent = HudScalePercent
    };

    internal void Apply(ApsSettingsData data)
    {
        if (!ApsSettingsData.IsSupportedSchemaVersion(data.SchemaVersion))
            throw new NotSupportedException($"Unsupported APS settings schema {data.SchemaVersion}.");
        var defaults = new ApsSettingsData();
        data = ApsSettingsData.MigrateLegacyDefaults(data);
        TemplateShape = DefinedOr(data.TemplateShape, defaults.TemplateShape);
        HoleShape = DefinedOr(data.HoleShape, defaults.HoleShape);
        SnapToOddDimensions = data.SnapToOddDimensions;
        Width = NormalizeHorizontalDimension(data.Width, MinimumTemplateDimension);
        Depth = NormalizeHorizontalDimension(data.Depth, MinimumDimension);
        TetrisType = DefinedOr(data.TetrisType, defaults.TetrisType);
        ComponentLength = TetrisType == TetrisType.FiveClip
            ? NormalizeFiveClipHeight(data.ComponentLength)
            : Math.Clamp(data.ComponentLength, MinimumComponentLength, MaximumComponentLength);
        SymmetryType = DefinedOr(data.SymmetryType, defaults.SymmetryType);
        if (SymmetryType == SymmetryType.HorizontalReflection)
            SymmetryType = SymmetryType.VerticalReflection;
        SymmetryMode = DefinedOr(data.SymmetryMode, defaults.SymmetryMode);
        MaxTimeSeconds = Math.Clamp(data.MaxTimeSeconds, MinimumSeconds, MaximumSeconds);
        MaxThreads = data.MaxThreads <= 0
            ? DefaultThreads
            : Math.Clamp(data.MaxThreads, 1, MaximumThreads);
        EarlyStopEnabled = data.EarlyStopEnabled;
        ExtraLayers = DefinedOr(data.ExtraLayers, defaults.ExtraLayers).ClampFor(TetrisType);
        TargetDensityPercent = NormalizeStep(data.TargetDensityPercent,
            MinimumDensityPercent, MaximumDensityPercent, DensityPercentStep);
        HudScalePercent = NormalizeStep(data.HudScalePercent,
            MinimumHudScalePercent, MaximumHudScalePercent, HudScalePercentStep);

        if (IsDepthLocked)
        {
            Depth = Width;
        }
        requestedHoleSize = data.HoleSize;
        HoleSize = NormalizeHoleSize(requestedHoleSize);
        EnsureSymmetryIsValid();
        ShrinkToSizeLimit();
    }

    internal void SetTemplateShape(ApsTemplateShape shape)
    {
        TemplateShape = shape;
        if (IsDepthLocked)
        {
            Width = NormalizeHorizontalDimension(Width, MinimumTemplateDimension);
            Depth = Width;
        }
        HoleSize = NormalizeHoleSize(requestedHoleSize);
        EnsureSymmetryIsValid();
        ShrinkToSizeLimit();
    }

    internal void AdjustWidth(int delta)
    {
        int step = SnapToOddDimensions ? Math.Sign(delta) * 2 : Math.Sign(delta);
        if (delta > 0 && !CanIncreaseWidth)
            return;
        SetWidth(Width + step);
    }

    /// <summary>False when one more width step would exceed the size limit for the current options.</summary>
    internal bool CanIncreaseWidth => Width < MaximumDimension && !WouldBeTooLarge(Width + WidthStep, IsDepthLocked ? Width + WidthStep : Depth);

    internal bool CanIncreaseDepth => !IsDepthLocked && Depth < MaximumDimension && !WouldBeTooLarge(Width, Depth + WidthStep);

    internal void SetWidth(int width)
    {
        Width = NormalizeHorizontalDimension(width, MinimumTemplateDimension);
        if (IsDepthLocked)
            Depth = Width;
        HoleSize = NormalizeHoleSize(requestedHoleSize);
        ShrinkWhileTooLarge(ShrinkWidth);
        EnsureSymmetryIsValid();
    }

    internal void AdjustDepth(int delta)
    {
        if (IsDepthLocked)
            return;
        int step = SnapToOddDimensions ? Math.Sign(delta) * 2 : Math.Sign(delta);
        if (delta > 0 && !CanIncreaseDepth)
            return;
        SetDepth(Depth + step);
    }

    internal void SetDepth(int depth)
    {
        if (IsDepthLocked)
            return;
        Depth = NormalizeHorizontalDimension(depth, MinimumDimension);
        ShrinkWhileTooLarge(ShrinkDepth);
        EnsureSymmetryIsValid();
    }

    internal void AdjustComponentLength(int delta) =>
        SetComponentLength(ComponentLength + Math.Sign(delta) * ComponentLengthStep);

    internal void SetComponentLength(int length) =>
        ComponentLength = TetrisType == TetrisType.FiveClip
            ? NormalizeFiveClipHeight(length)
            : Math.Clamp(length, MinimumComponentLength, MaximumComponentLength);

    internal void SetHoleShape(CenterHoleShape shape) => HoleShape = shape;

    internal void AdjustHoleSize(int delta)
    {
        if (delta == 0)
            return;

        SetHoleSize(NormalizeHoleSize(HoleSize + Math.Sign(delta) * 2));
    }

    internal void SetHoleSize(int size)
    {
        requestedHoleSize = size;
        HoleSize = NormalizeHoleSize(requestedHoleSize);
    }

    internal void SetAllowEvenDimensions(bool enabled) => SetSnapToOddDimensions(!enabled);

    internal void SetSnapToOddDimensions(bool enabled)
    {
        SnapToOddDimensions = enabled;
        if (!enabled)
            return;

        Width = NormalizeHorizontalDimension(Width, MinimumTemplateDimension);
        Depth = IsDepthLocked
            ? Width
            : NormalizeHorizontalDimension(Depth, MinimumDimension);
        HoleSize = NormalizeHoleSize(requestedHoleSize);
        EnsureSymmetryIsValid();
        ShrinkToSizeLimit();
    }

    internal void SetTetrisType(TetrisType type)
    {
        TetrisType = type;
        ExtraLayers = ExtraLayers.ClampFor(type);
        ComponentLength = type == TetrisType.FiveClip
            ? NormalizeFiveClipHeight(ComponentLength)
            : Math.Clamp(ComponentLength, MinimumComponentLength, MaximumComponentLength);
        ShrinkToSizeLimit();
    }

    internal void SetExtraLayers(ExportExtraLayers layers) =>
        ExtraLayers = layers.ClampFor(TetrisType);

    internal void SetSymmetryType(SymmetryType symmetry)
    {
        if (symmetry == SymmetryType.HorizontalReflection)
            symmetry = SymmetryType.VerticalReflection;
        if (symmetry == SymmetryType.Rotation90 && Width != Depth)
            return;
        SymmetryType = symmetry;
        ShrinkToSizeLimit();
    }

    internal void SetSymmetryMode(SymmetryMode mode)
    {
        SymmetryMode = mode;
        ShrinkToSizeLimit();
    }

    internal void AdjustMaxTime(int delta) =>
        MaxTimeSeconds = Math.Clamp(MaxTimeSeconds + delta, MinimumSeconds, MaximumSeconds);

    internal void AdjustThreads(int delta) =>
        MaxThreads = Math.Clamp(MaxThreads + delta, 1, MaximumThreads);

    internal void SetEarlyStop(bool enabled) => EarlyStopEnabled = enabled;

    internal void SetTargetDensityPercent(int percent) =>
        TargetDensityPercent = NormalizeStep(percent, MinimumDensityPercent, MaximumDensityPercent, DensityPercentStep);

    internal void AdjustHudScale(int delta) =>
        HudScalePercent = NormalizeStep(HudScalePercent + Math.Sign(delta) * HudScalePercentStep,
            MinimumHudScalePercent, MaximumHudScalePercent, HudScalePercentStep);

    internal ApsGenerationRequest CreateRequest() => new(
        new TetrisProblem(
            new ApsTemplate(TemplateShape, Width, Depth, HoleShape, HoleSize),
            TetrisType,
            SymmetryType,
            SymmetryMode,
            TargetDensityPercent),
        new ApsOutputOptions(ComponentLength, ExtraLayers),
        new ApsSolveLimits(MaxTimeSeconds, MaxThreads, EarlyStopEnabled));

    private int WidthStep => SnapToOddDimensions ? 2 : 1;

    /// <summary>Brings the template back within the size limit after an option lowered it.</summary>
    private void ShrinkToSizeLimit()
    {
        ShrinkWhileTooLarge(() => !IsDepthLocked && Depth > Width ? ShrinkDepth() : ShrinkWidth());
        EnsureSymmetryIsValid();
    }

    private void ShrinkWhileTooLarge(Func<bool> shrinkOnce)
    {
        while (IsTooLarge && shrinkOnce())
        {
        }
    }

    private bool ShrinkWidth()
    {
        int width = NormalizeHorizontalDimension(Width - WidthStep, MinimumTemplateDimension);
        if (width >= Width)
            return false;
        Width = width;
        if (IsDepthLocked)
            Depth = Width;
        HoleSize = NormalizeHoleSize(requestedHoleSize);
        return true;
    }

    private bool ShrinkDepth()
    {
        int depth = NormalizeHorizontalDimension(Depth - WidthStep, MinimumDimension);
        if (IsDepthLocked || depth >= Depth)
            return false;
        Depth = depth;
        return true;
    }

    private bool WouldBeTooLarge(int width, int depth)
    {
        var template = new ApsTemplate(TemplateShape, Math.Min(width, MaximumDimension),
            Math.Min(depth, MaximumDimension), HoleShape, HoleSize);
        return ApsSolveSizeLimits.Classify(template.AvailableCells, TetrisType, SymmetryType, SymmetryMode) ==
               ApsSolveSize.TooLarge;
    }

    private static int NormalizeStep(int value, int minimum, int maximum, int step)
    {
        int clamped = Math.Clamp(value, minimum, maximum);
        return Math.Clamp(minimum + (int)Math.Round((clamped - minimum) / (double)step) * step, minimum, maximum);
    }

    private static T DefinedOr<T>(T value, T fallback) where T : struct, Enum =>
        Enum.IsDefined(typeof(T), value) ? value : fallback;

    private void EnsureSymmetryIsValid()
    {
        if (SymmetryType == SymmetryType.Rotation90 && Width != Depth)
            SymmetryType = SymmetryType.Rotation180;
    }

    private int NormalizeHorizontalDimension(int dimension, int minimum)
    {
        int clamped = Math.Clamp(dimension, minimum, MaximumDimension);
        if (!SnapToOddDimensions || clamped % 2 != 0)
            return clamped;
        return clamped < MaximumDimension ? clamped + 1 : clamped - 1;
    }

    private int NormalizeHoleSize(int size) => NormalizeHoleSizeFor(Width, size);

    private static int NormalizeHoleSizeFor(int width, int size)
    {
        int minimum = width % 2 == 0 ? 2 : 1;
        int maximum = Math.Max(minimum, width - 4);
        int clamped = Math.Clamp(size, minimum, maximum);
        if (clamped % 2 == width % 2)
            return clamped;
        if (clamped + 1 <= maximum)
            return clamped + 1;
        return Math.Max(minimum, clamped - 1);
    }

    private int MinimumTemplateDimension =>
        TemplateShape == ApsTemplateShape.CircleCenterHole ? MinimumCenterHoleDimension : MinimumDimension;

    private int ComponentLengthStep =>
        TetrisType == TetrisType.FiveClip ? FiveClipSectionHeight : 1;

    private static int NormalizeFiveClipHeight(int height)
    {
        int clamped = Math.Clamp(height, MinimumFiveClipHeight, MaximumFiveClipHeight);
        return Math.Clamp(
            (int)Math.Round(clamped / (double)FiveClipSectionHeight) * FiveClipSectionHeight,
            MinimumFiveClipHeight,
            MaximumFiveClipHeight);
    }
}

internal sealed class ApsSettingsData
{
    internal const int OldestSupportedSchemaVersion = 1;
    /// <summary>Version 2 changed the solver defaults; version 1 profiles are migrated on load.</summary>
    internal const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Moves version 1 values that still equal the version 1 defaults to the current defaults.
    /// </summary>
    internal static ApsSettingsData MigrateLegacyDefaults(ApsSettingsData data)
    {
        if (data.SchemaVersion >= CurrentSchemaVersion)
            return data;
        var current = new ApsSettingsData();
        var migrated = (ApsSettingsData)data.MemberwiseClone();
        migrated.SchemaVersion = CurrentSchemaVersion;
        if (data.SymmetryType == ApsGenerationDefaults.Legacy.DefaultSymmetryType)
            migrated.SymmetryType = current.SymmetryType;
        if (data.MaxTimeSeconds == ApsGenerationDefaults.Legacy.MaxTimeSeconds)
            migrated.MaxTimeSeconds = current.MaxTimeSeconds;
        if (data.EarlyStopEnabled == ApsGenerationDefaults.Legacy.EarlyStopEnabled)
            migrated.EarlyStopEnabled = current.EarlyStopEnabled;
        return migrated;
    }

    internal static bool IsSupportedSchemaVersion(int schemaVersion) =>
        schemaVersion is >= OldestSupportedSchemaVersion and <= CurrentSchemaVersion;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public ApsTemplateShape TemplateShape { get; set; } = ApsTemplateShape.CircleCenterHole;
    public int Width { get; set; } = ApsGenerationDefaults.TemplateWidth;
    public int Depth { get; set; } = ApsGenerationDefaults.TemplateDepth;
    public int ComponentLength { get; set; } = ApsGenerationDefaults.BasicComponentHeight;
    public CenterHoleShape HoleShape { get; set; } = ApsGenerationDefaults.DefaultCenterHoleShape;
    public int HoleSize { get; set; } = ApsGenerationDefaults.CenterHoleSize;
    public bool SnapToOddDimensions { get; set; } = false;
    public TetrisType TetrisType { get; set; } = ApsGenerationDefaults.DefaultTetrisType;
    public SymmetryType SymmetryType { get; set; } = ApsGenerationDefaults.DefaultSymmetryType;
    public SymmetryMode SymmetryMode { get; set; } = ApsGenerationDefaults.DefaultSymmetryMode;
    public int MaxTimeSeconds { get; set; } = ApsGenerationDefaults.MaxTimeSeconds;
    // Zero means "use the default share of the processors" so the preference follows CPU-count changes.
    public int MaxThreads { get; set; }
    public bool EarlyStopEnabled { get; set; } = ApsGenerationDefaults.EarlyStopEnabled;
    public ExportExtraLayers ExtraLayers { get; set; } = ApsGenerationDefaults.DefaultExtraLayers;
    public int TargetDensityPercent { get; set; } = ApsGenerationSettings.MaximumDensityPercent;
    public int HudScalePercent { get; set; } = 100;
}
