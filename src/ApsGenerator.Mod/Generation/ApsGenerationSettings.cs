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

    internal bool IsDepthLocked => TemplateShape != ApsTemplateShape.Rectangle;
    internal int MaximumThreads => Math.Max(1, Environment.ProcessorCount - 1);
    internal int RecommendedMaximumDimension => TetrisType switch
    {
        TetrisType.FiveClip => 15,
        TetrisType.FourClip => 19,
        _ => 23
    };
    internal bool IsUnusuallyLarge =>
        Width > RecommendedMaximumDimension || Depth > RecommendedMaximumDimension;

    internal void ResetToDefaults() => Apply(new ApsSettingsData());

    internal ApsSettingsChangeImpact Change(Action change)
    {
        ApsGenerationRequest previous = CreateRequest();
        bool previousOddSnapping = SnapToOddDimensions;
        change();
        ApsGenerationRequest current = CreateRequest();
        if (previous == current)
            return previousOddSnapping != SnapToOddDimensions
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
        MaxThreads = MaxThreads == MaximumThreads ? 0 : MaxThreads,
        EarlyStopEnabled = EarlyStopEnabled,
        ExtraLayers = ExtraLayers
    };

    internal void Apply(ApsSettingsData data)
    {
        if (!ApsSettingsData.IsSupportedSchemaVersion(data.SchemaVersion))
            throw new NotSupportedException($"Unsupported APS settings schema {data.SchemaVersion}.");
        var defaults = new ApsSettingsData();
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
            ? MaximumThreads
            : Math.Clamp(data.MaxThreads, 1, MaximumThreads);
        EarlyStopEnabled = data.EarlyStopEnabled;
        ExtraLayers = DefinedOr(data.ExtraLayers, defaults.ExtraLayers).ClampFor(TetrisType);

        if (IsDepthLocked)
        {
            Depth = Width;
        }
        requestedHoleSize = data.HoleSize;
        HoleSize = NormalizeHoleSize(requestedHoleSize);
        EnsureSymmetryIsValid();
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
    }

    internal void AdjustWidth(int delta)
    {
        int step = SnapToOddDimensions ? Math.Sign(delta) * 2 : Math.Sign(delta);
        SetWidth(Width + step);
    }

    internal void SetWidth(int width)
    {
        Width = NormalizeHorizontalDimension(width, MinimumTemplateDimension);
        if (IsDepthLocked)
            Depth = Width;
        HoleSize = NormalizeHoleSize(requestedHoleSize);
        EnsureSymmetryIsValid();
    }

    internal void AdjustDepth(int delta)
    {
        if (IsDepthLocked)
            return;
        int step = SnapToOddDimensions ? Math.Sign(delta) * 2 : Math.Sign(delta);
        SetDepth(Depth + step);
    }

    internal void SetDepth(int depth)
    {
        if (IsDepthLocked)
            return;
        Depth = NormalizeHorizontalDimension(depth, MinimumDimension);
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
    }

    internal void SetTetrisType(TetrisType type)
    {
        TetrisType = type;
        ExtraLayers = ExtraLayers.ClampFor(type);
        ComponentLength = type == TetrisType.FiveClip
            ? NormalizeFiveClipHeight(ComponentLength)
            : Math.Clamp(ComponentLength, MinimumComponentLength, MaximumComponentLength);
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
    }

    internal void SetSymmetryMode(SymmetryMode mode) => SymmetryMode = mode;

    internal void AdjustMaxTime(int delta) =>
        MaxTimeSeconds = Math.Clamp(MaxTimeSeconds + delta, MinimumSeconds, MaximumSeconds);

    internal void AdjustThreads(int delta) =>
        MaxThreads = Math.Clamp(MaxThreads + delta, 1, MaximumThreads);

    internal void SetEarlyStop(bool enabled) => EarlyStopEnabled = enabled;

    internal ApsGenerationRequest CreateRequest() => new(
        new TetrisProblem(
            new ApsTemplate(TemplateShape, Width, Depth, HoleShape, HoleSize),
            TetrisType,
            SymmetryType,
            SymmetryMode),
        new ApsOutputOptions(ComponentLength, ExtraLayers),
        new ApsSolveLimits(MaxTimeSeconds, MaxThreads, EarlyStopEnabled));

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

    private int NormalizeHoleSize(int size)
    {
        int minimum = Width % 2 == 0 ? 2 : 1;
        int maximum = Math.Max(minimum, Width - 4);
        int clamped = Math.Clamp(size, minimum, maximum);
        if (clamped % 2 == Width % 2)
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
    internal const int CurrentSchemaVersion = 1;

    internal static bool IsSupportedSchemaVersion(int schemaVersion) =>
        schemaVersion is >= OldestSupportedSchemaVersion and <= CurrentSchemaVersion;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public ApsTemplateShape TemplateShape { get; set; } = ApsTemplateShape.CircleCenterHole;
    public int Width { get; set; } = 15;
    public int Depth { get; set; } = 15;
    public int ComponentLength { get; set; } = 3;
    public CenterHoleShape HoleShape { get; set; } = CenterHoleShape.Circle;
    public int HoleSize { get; set; } = 1;
    public bool SnapToOddDimensions { get; set; } = false;
    public TetrisType TetrisType { get; set; } = TetrisType.ThreeClip;
    public SymmetryType SymmetryType { get; set; } = SymmetryType.None;
    public SymmetryMode SymmetryMode { get; set; } = SymmetryMode.Hard;
    public int MaxTimeSeconds { get; set; } = 30;
    // Zero means "use the maximum" so the preference follows CPU-count changes.
    public int MaxThreads { get; set; }
    public bool EarlyStopEnabled { get; set; } = true;
    public ExportExtraLayers ExtraLayers { get; set; } = ExportExtraLayers.EjectorsIntakesCoolerSnake;
}
