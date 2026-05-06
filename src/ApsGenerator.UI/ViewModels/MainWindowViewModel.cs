using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver;
using ApsGenerator.UI.Models;
using ApsGenerator.UI.Services;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiTemplateShape = ApsGenerator.UI.Models.TemplateShape;

namespace ApsGenerator.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const int MinTemplateDimension = 3;
    private const int MaxTemplateDimension = 50;
    private const double MinSolverSeconds = 1;
    private const double MaxSolverSeconds = 600;

    private static readonly IBrush DefaultStatusBrush = new SolidColorBrush(Color.Parse("#9E9E9E"));
    private static readonly IBrush OptimalBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush LikelyOptimalBrush = new SolidColorBrush(Color.Parse("#8BC34A"));
    private static readonly IBrush TimedOutBrush = new SolidColorBrush(Color.Parse("#FF9800"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#F44336"));

    [ObservableProperty]
    private EnumDisplayItem<UiTemplateShape> templateShape = TemplateShapeValues.All[1];

    [ObservableProperty]
    private int templateWidth = 15;

    [ObservableProperty]
    private int templateHeight = 15;

    [ObservableProperty]
    private bool isHeightLocked = true;

    [ObservableProperty]
    private PaintMode paintMode = PaintMode.Block;

    [ObservableProperty]
    private EnumDisplayItem<TetrisType> selectedTetrisType = EnumValues.TetrisTypes[0];

    [ObservableProperty]
    private EnumDisplayItem<SymmetryType> selectedSymmetryType = EnumValues.SymmetryTypes[0];

    [ObservableProperty]
    private bool isHardSymmetry = true;

    [ObservableProperty]
    private double maxTimeSeconds = 30;

    [ObservableProperty]
    private double uiScale = 1.0;

    [ObservableProperty]
    private bool earlyStopEnabled = true;

    [ObservableProperty]
    private int targetPlacementCount;

    [ObservableProperty]
    private int maxPlacements;

    [ObservableProperty]
    private bool isGenerating;

    [ObservableProperty]
    private Grid grid = TemplateGenerator.Circle(15, true);

    [ObservableProperty]
    private SolverResult? solverResult;

    [ObservableProperty]
    private string placedText = "";

    [ObservableProperty]
    private string statusLabel = "Ready";

    [ObservableProperty]
    private string statusDetailText = "";

    [ObservableProperty]
    private string elapsedTimeText = "";

    [ObservableProperty]
    private IBrush statusForeground = DefaultStatusBrush;

    private bool isGridDirty;
    private bool isConfirmDialogOpen;
    private CancellationTokenSource? cancellationTokenSource;
    private bool suppressRegenerate;
    private DispatcherTimer? elapsedTimer;
    private System.Diagnostics.Stopwatch? solveStopwatch;
    private bool suppressRatioUpdate;
    private double targetRatio = 1.0;
    private bool hasTargetRatio;
    private bool applyMaximizeFromSettings;

    [ObservableProperty]
    private bool canExport;

    [ObservableProperty]
    private int numSolutions = 1;

    [ObservableProperty]
    private int currentSolutionIndex;

    private IReadOnlyList<IReadOnlyList<Placement>> allTrimmedSolutions = [];

    public string SolutionCounterText =>
        allTrimmedSolutions.Count > 1
            ? $"{CurrentSolutionIndex + 1} / {allTrimmedSolutions.Count}"
            : "";

    public bool HasMultipleSolutions => allTrimmedSolutions.Count > 1;

    partial void OnSolverResultChanged(SolverResult? value)
    {
        if (value is null)
        {
            allTrimmedSolutions = [];
            CurrentSolutionIndex = 0;
            OnPropertyChanged(nameof(HasMultipleSolutions));
            OnPropertyChanged(nameof(SolutionCounterText));
        }
    }

    [ObservableProperty]
    private int threadCount = Math.Max(1, Environment.ProcessorCount - 1);

    [ObservableProperty]
    private int defaultExportHeightBasic = 2;

    [ObservableProperty]
    private int defaultExportHeightFiveClip = 3;

    [ObservableProperty]
    private string exportNameTemplate = UserSettings.DefaultExportNameTemplate;

    public int MaxThreadCount => Math.Max(1, Environment.ProcessorCount - 1);

    public Func<string, Task<bool>>? ConfirmAsync { get; set; }

    public Func<Task>? ShowExportDialogAsync { get; set; }

    public Action<double>? ScaleChanged { get; set; }

    public int SliderMaximum => MaxPlacements;

    public int SliderMinimum => MaxPlacements / 3;

    public bool IsHeightEditable => TemplateShape.Value == UiTemplateShape.Rectangle && !IsHeightLocked;

    public bool IsSymmetryEnabled => SelectedSymmetryType.Value != SymmetryType.None;

    public bool IsMaximize => MaxPlacements > 0 && TargetPlacementCount >= MaxPlacements;

    public bool IsRotation90NonSquareWarning =>
        SelectedSymmetryType.Value == SymmetryType.Rotation90 && Grid.Width != Grid.Height;

    public bool IsPaintModeBlock => PaintMode == PaintMode.Block;
    public bool IsPaintModeClear => PaintMode == PaintMode.Clear;
    public bool IsPaintModeToggle => PaintMode == PaintMode.Toggle;

    public string DensityDisplayText
    {
        get
        {
            if (MaxPlacements == 0)
                return "0 / 0 (0%)";

            if (IsMaximize)
                return "Maximize";

            var percent = (int)Math.Round(100.0 * TargetPlacementCount / MaxPlacements);
            return $"{TargetPlacementCount} / {MaxPlacements} ({percent}%)";
        }
    }

    public MainWindowViewModel()
    {
        suppressRegenerate = true;
        ApplyUserSettings(UserSettingsStore.Load());
        suppressRegenerate = false;
        RegenerateGrid();
    }

    public UserSettings CreateUserSettings() => new()
    {
        TemplateShape = TemplateShape.Value,
        TemplateWidth = TemplateWidth,
        TemplateHeight = TemplateHeight,
        IsHeightLocked = IsHeightLocked,
        SelectedTetrisType = SelectedTetrisType.Value,
        SelectedSymmetryType = SelectedSymmetryType.Value,
        IsHardSymmetry = IsHardSymmetry,
        EarlyStopEnabled = EarlyStopEnabled,
        MaxTimeSeconds = MaxTimeSeconds,
        IsMaximize = IsMaximize,
        TargetPlacementCount = TargetPlacementCount,
        PaintMode = PaintMode,
        LastExportFolder = LastExportFolder,
        ThreadCount = ThreadCount,
        DefaultExportHeightBasic = DefaultExportHeightBasic,
        DefaultExportHeightFiveClip = DefaultExportHeightFiveClip,
        ExportNameTemplate = ExportNameTemplate,
        NumSolutions = NumSolutions,
        UiScale = UiScale
    };

    partial void OnDefaultExportHeightFiveClipChanged(int value)
    {
        int clampedValue = FiveClipHeight.RoundToMultipleOf3(value);
        if (value == clampedValue)
            return;

        DefaultExportHeightFiveClip = clampedValue;
    }

    private void ApplyUserSettings(UserSettings settings)
    {
        var defaults = new UserSettings();

        TemplateShape = ResolveDisplayItem(
            TemplateShapeValues.All,
            settings.TemplateShape,
            defaults.TemplateShape,
            TemplateShapeValues.All[1]);

        TemplateWidth = ResolveInt(
            settings.TemplateWidth,
            defaults.TemplateWidth,
            MinTemplateDimension,
            MaxTemplateDimension);

        TemplateHeight = ResolveInt(
            settings.TemplateHeight,
            defaults.TemplateHeight,
            MinTemplateDimension,
            MaxTemplateDimension);

        IsHeightLocked = settings.IsHeightLocked;
        if (IsHeightLocked)
            TemplateHeight = TemplateWidth;

        SelectedTetrisType = ResolveDisplayItem(
            EnumValues.TetrisTypes,
            settings.SelectedTetrisType,
            defaults.SelectedTetrisType,
            EnumValues.TetrisTypes[0]);

        SelectedSymmetryType = ResolveDisplayItem(
            EnumValues.SymmetryTypes,
            settings.SelectedSymmetryType,
            defaults.SelectedSymmetryType,
            EnumValues.SymmetryTypes[0]);

        IsHardSymmetry = settings.IsHardSymmetry;
        EarlyStopEnabled = settings.EarlyStopEnabled;
        MaxTimeSeconds = ResolveDouble(
            settings.MaxTimeSeconds,
            defaults.MaxTimeSeconds,
            MinSolverSeconds,
            MaxSolverSeconds);
        UiScale = settings.UiScale;

        TargetPlacementCount = settings.TargetPlacementCount >= 0
            ? settings.TargetPlacementCount
            : defaults.TargetPlacementCount;
        applyMaximizeFromSettings = settings.IsMaximize;

        PaintMode = ResolveEnum(settings.PaintMode, defaults.PaintMode);
        LastExportFolder = settings.LastExportFolder;
        ThreadCount = ResolveInt(settings.ThreadCount, defaults.ThreadCount, 1, MaxThreadCount);
        DefaultExportHeightBasic = ResolveInt(settings.DefaultExportHeightBasic, defaults.DefaultExportHeightBasic, 1, 8);
        int resolvedDefaultFiveClip = ResolveInt(
            settings.DefaultExportHeightFiveClip,
            defaults.DefaultExportHeightFiveClip,
            FiveClipHeight.MinHeight,
            FiveClipHeight.MaxHeight);
        DefaultExportHeightFiveClip = FiveClipHeight.RoundToMultipleOf3(resolvedDefaultFiveClip);
        ExportNameTemplate = string.IsNullOrWhiteSpace(settings.ExportNameTemplate)
            ? defaults.ExportNameTemplate
            : settings.ExportNameTemplate;
        NumSolutions = ResolveInt(settings.NumSolutions, defaults.NumSolutions, 1, 50);
    }

    private static EnumDisplayItem<TEnum> ResolveDisplayItem<TEnum>(
        IReadOnlyList<EnumDisplayItem<TEnum>> values,
        TEnum settingValue,
        TEnum defaultValue,
        EnumDisplayItem<TEnum> fallback)
        where TEnum : struct, Enum
    {
        var resolvedValue = ResolveEnum(settingValue, defaultValue);
        foreach (var item in values)
        {
            if (EqualityComparer<TEnum>.Default.Equals(item.Value, resolvedValue))
                return item;
        }

        return fallback;
    }

    private static TEnum ResolveEnum<TEnum>(TEnum settingValue, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.IsDefined(settingValue) ? settingValue : fallback;

    private static int ResolveInt(int settingValue, int fallback, int min, int max)
    {
        if (settingValue < min || settingValue > max)
            return fallback;

        return settingValue;
    }

    private static double ResolveDouble(double settingValue, double fallback, double min, double max)
    {
        if (double.IsNaN(settingValue) || double.IsInfinity(settingValue))
            return fallback;

        if (settingValue < min || settingValue > max)
            return fallback;

        return settingValue;
    }

    private void RegenerateGrid()
    {
        if (suppressRegenerate)
            return;

        Grid = TemplateShape.Value switch
        {
            UiTemplateShape.Circle => TemplateGenerator.Circle(TemplateWidth, blockCenter: false),
            UiTemplateShape.CircleCenterHole => TemplateGenerator.Circle(TemplateWidth, blockCenter: true),
            UiTemplateShape.Rectangle => TemplateGenerator.Rectangle(TemplateWidth, TemplateHeight),
            _ => throw new InvalidOperationException($"Unknown template shape: {TemplateShape.Value}")
        };

        SolverResult = null;
        CanExport = false;
        isGridDirty = false;
        ClearStatus();
    }

    private void ClearStatus()
    {
        PlacedText = "";
        StatusLabel = "Ready";
        ElapsedTimeText = "";
        StatusDetailText = "";
        StatusForeground = DefaultStatusBrush;
    }

    private async Task MaybeRegenerateWithConfirmationAsync(Action? revertAction = null)
    {
        if (suppressRegenerate)
            return;

        if (ConfirmAsync is null)
        {
            RegenerateGrid();
            return;
        }

        try
        {
            if (isGridDirty && !isConfirmDialogOpen)
            {
                isConfirmDialogOpen = true;
                var confirmed = await ConfirmAsync(
                    "Changing the template will discard your manual edits. Continue?");
                if (!confirmed)
                {
                    revertAction?.Invoke();
                    return;
                }
            }

            RegenerateGrid();
        }
        catch (Exception ex)
        {
            StatusLabel = "Error";
            StatusDetailText = ex.Message;
            StatusForeground = ErrorBrush;
        }
        finally
        {
            isConfirmDialogOpen = false;
        }
    }

    private void RecomputeMaxPlacements()
    {
        int newMaxPlacements = GetTheoreticalMaxClusters(SelectedTetrisType.Value, Grid.AvailableCellCount);
        MaxPlacements = newMaxPlacements;

        if (applyMaximizeFromSettings)
        {
            applyMaximizeFromSettings = false;
            suppressRatioUpdate = true;
            try
            {
                TargetPlacementCount = MaxPlacements;
            }
            finally
            {
                suppressRatioUpdate = false;
            }

            targetRatio = 1.0;
            hasTargetRatio = true;
        }
        else if (IsMaximize)
        {
            suppressRatioUpdate = true;
            try
            {
                TargetPlacementCount = Math.Clamp(TargetPlacementCount, SliderMinimum, MaxPlacements);
            }
            finally
            {
                suppressRatioUpdate = false;
            }
        }
        else
        {
            EnsureTargetRatioInitialized();
            SetTargetPlacementCountFromRatio();
        }

        OnPropertyChanged(nameof(SliderMaximum));
        OnPropertyChanged(nameof(SliderMinimum));
        OnPropertyChanged(nameof(DensityDisplayText));
        OnPropertyChanged(nameof(IsMaximize));
    }

    private void EnsureTargetRatioInitialized()
    {
        if (hasTargetRatio)
            return;

        targetRatio = MaxPlacements > 0
            ? (double)Math.Clamp(TargetPlacementCount, SliderMinimum, MaxPlacements) / MaxPlacements
            : 1.0;

        hasTargetRatio = true;
    }

    private void SetTargetPlacementCountFromRatio()
    {
        int scaledTarget = (int)Math.Round(targetRatio * MaxPlacements);
        int clampedTarget = Math.Clamp(scaledTarget, SliderMinimum, MaxPlacements);

        suppressRatioUpdate = true;
        try
        {
            TargetPlacementCount = clampedTarget;
        }
        finally
        {
            suppressRatioUpdate = false;
        }
    }

    private static int GetTheoreticalMaxClusters(TetrisType type, int availableCells) => type switch
    {
        TetrisType.ThreeClip => availableCells / 4,
        TetrisType.FourClip => availableCells / 5,
        TetrisType.FiveClip => (2 * availableCells) / 9,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported tetris type.")
    };

    [RelayCommand]
    private void SetPaintMode(PaintMode mode) => PaintMode = mode;

    [RelayCommand(CanExecute = nameof(CanResetToTemplate))]
    private void ResetToTemplate() => RegenerateGrid();

    private bool CanResetToTemplate() => !IsGenerating;

    [RelayCommand]
    private void PaintCell((int Row, int Col) cell)
    {
        if (!Grid.IsInBounds(cell.Row, cell.Col))
            return;

        CellState newState = PaintMode switch
        {
            PaintMode.Toggle => Grid[cell.Row, cell.Col] == CellState.Available
                ? CellState.Blocked
                : CellState.Available,
            PaintMode.Clear => CellState.Available,
            _ => CellState.Blocked
        };

        var symmetryType = SelectedSymmetryType.Value;
        var positions = new List<(int Row, int Col)> { (cell.Row, cell.Col) };
        if (symmetryType != SymmetryType.None)
        {
            if (symmetryType == SymmetryType.Rotation90 && Grid.Width != Grid.Height)
            {
                StatusLabel = "Rotation90 requires square grid";
                StatusForeground = TimedOutBrush;
            }
            else
            {
                positions = SymmetryTransforms.GetSymmetricPositions(
                    cell.Row, cell.Col, Grid.Width, Grid.Height, symmetryType).ToList();
            }
        }

        foreach (var (row, col) in positions)
        {
            if (Grid.IsInBounds(row, col))
                Grid[row, col] = newState;
        }

        isGridDirty = true;
        SolverResult = null;
        CanExport = false;
        OnPropertyChanged(nameof(Grid));
        RecomputeMaxPlacements();
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        IsGenerating = true;
        PlacedText = "";
        StatusLabel = "Solving...";
        ElapsedTimeText = "";
        StatusDetailText = "";
        StatusForeground = DefaultStatusBrush;
        SolverResult = null;
        allTrimmedSolutions = [];
        CurrentSolutionIndex = 0;
        OnPropertyChanged(nameof(HasMultipleSolutions));
        OnPropertyChanged(nameof(SolutionCounterText));
        cancellationTokenSource = new CancellationTokenSource();
        var ct = cancellationTokenSource.Token;

        solveStopwatch = System.Diagnostics.Stopwatch.StartNew();
        elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        elapsedTimer.Tick += (_, _) => ElapsedTimeText = FormatDuration(solveStopwatch?.Elapsed ?? TimeSpan.Zero);
        elapsedTimer.Start();

        try
        {
            var solver = new TetrisSolver();
            var options = new SolverOptions
            {
                MaxThreads = ThreadCount,
                MaxTimeSeconds = MaxTimeSeconds,
                SymmetryType = SelectedSymmetryType.Value,
                SymmetryMode = IsHardSymmetry ? SymmetryMode.Hard : SymmetryMode.Soft,
                EarlyStopEnabled = EarlyStopEnabled,
                TargetClusterCount = IsMaximize ? null : TargetPlacementCount,
                NumSolutions = NumSolutions
            };
            var gridSnapshot = Grid.Clone();
            var tetrisType = SelectedTetrisType.Value;
            var result = await Task.Run(
                () => solver.Solve(gridSnapshot, tetrisType, options, ct), ct);

            if (!IsMaximize && result.Placements.Count > 0)
            {
                var trimmedSolutions = new List<IReadOnlyList<Placement>>();
                foreach (var solution in result.AllSolutions)
                {
                    var trimmed = PlacementTrimmer.Trim(
                        solution, gridSnapshot, tetrisType,
                        SelectedSymmetryType.Value, TargetPlacementCount);
                    trimmedSolutions.Add(trimmed);
                }

                var firstTrimmed = trimmedSolutions[0];
                var shapes = ClusterShape.GetShapes(tetrisType);
                var covered = new HashSet<(int, int)>();
                foreach (var p in firstTrimmed)
                    foreach (var o in shapes[p.ShapeIndex].Offsets)
                        covered.Add((p.Row + o.DeltaRow, p.Col + o.DeltaCol));

                result = new SolverResult
                {
                    Placements = firstTrimmed,
                    AllSolutions = trimmedSolutions,
                    EmptyCells = gridSnapshot.AvailableCellCount - covered.Count,
                    Status = result.Status
                };
            }

            allTrimmedSolutions = result.AllSolutions;
            CurrentSolutionIndex = 0;
            OnPropertyChanged(nameof(HasMultipleSolutions));
            OnPropertyChanged(nameof(SolutionCounterText));
            NextSolutionCommand.NotifyCanExecuteChanged();
            PrevSolutionCommand.NotifyCanExecuteChanged();

            SolverResult = result;
            CanExport = result.Placements.Count > 0;
            PlacedText = $"{result.ClusterCount} / {MaxPlacements}";
            StatusLabel = result.Status switch
            {
                SolverStatus.Optimal => "Optimal",
                SolverStatus.LikelyOptimal => "Likely Optimal",
                SolverStatus.TimedOut => "Timed Out",
                _ => result.Status.ToString()
            };
            ElapsedTimeText = FormatDuration(solveStopwatch.Elapsed);
            StatusForeground = GetStatusBrush(result.Status);
        }
        catch (OperationCanceledException)
        {
            StatusLabel = "Cancelled";
            ElapsedTimeText = FormatDuration(solveStopwatch.Elapsed);
            StatusForeground = DefaultStatusBrush;
        }
        catch (Exception ex)
        {
            StatusLabel = "Error";
            StatusDetailText = ex.Message;
            ElapsedTimeText = FormatDuration(solveStopwatch.Elapsed);
            StatusForeground = ErrorBrush;
        }
        finally
        {
            elapsedTimer?.Stop();
            elapsedTimer = null;
            solveStopwatch?.Stop();
            solveStopwatch = null;
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            IsGenerating = false;
        }
    }

    private bool CanGenerate() => !IsGenerating && !IsRotation90NonSquareWarning;

    private static IBrush GetStatusBrush(SolverStatus status) => status switch
    {
        SolverStatus.Optimal => OptimalBrush,
        SolverStatus.LikelyOptimal => LikelyOptimalBrush,
        SolverStatus.TimedOut => TimedOutBrush,
        _ => DefaultStatusBrush
    };

    [RelayCommand(CanExecute = nameof(IsGenerating))]
    private void Cancel()
    {
        cancellationTokenSource?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (ShowExportDialogAsync is not null)
            await ShowExportDialogAsync();
    }

    public string? LastExportFolder { get; set; }

    [RelayCommand(CanExecute = nameof(CanGoNextSolution))]
    private void NextSolution()
    {
        if (CurrentSolutionIndex < allTrimmedSolutions.Count - 1)
            CurrentSolutionIndex++;
    }

    private bool CanGoNextSolution() => CurrentSolutionIndex < allTrimmedSolutions.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoPrevSolution))]
    private void PrevSolution()
    {
        if (CurrentSolutionIndex > 0)
            CurrentSolutionIndex--;
    }

    private bool CanGoPrevSolution() => CurrentSolutionIndex > 0;

    partial void OnCurrentSolutionIndexChanged(int value)
    {
        NextSolutionCommand.NotifyCanExecuteChanged();
        PrevSolutionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SolutionCounterText));
        ApplyCurrentSolution();
    }

    private void ApplyCurrentSolution()
    {
        if (allTrimmedSolutions.Count == 0 || CurrentSolutionIndex >= allTrimmedSolutions.Count)
            return;

        var placements = allTrimmedSolutions[CurrentSolutionIndex];
        if (SolverResult is null)
            return;

        var shapes = ClusterShape.GetShapes(SelectedTetrisType.Value);
        var covered = new HashSet<(int, int)>();
        foreach (var p in placements)
            foreach (var o in shapes[p.ShapeIndex].Offsets)
                covered.Add((p.Row + o.DeltaRow, p.Col + o.DeltaCol));

        SolverResult = new SolverResult
        {
            Placements = placements,
            AllSolutions = allTrimmedSolutions,
            EmptyCells = Grid.AvailableCellCount - covered.Count,
            Status = SolverResult.Status
        };

        CanExport = placements.Count > 0;
        PlacedText = $"{placements.Count} / {MaxPlacements}";
    }

    partial void OnIsGeneratingChanged(bool value)
    {
        GenerateCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ResetToTemplateCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnTemplateShapeChanged(
        EnumDisplayItem<UiTemplateShape>? oldValue, EnumDisplayItem<UiTemplateShape> newValue)
    {
        OnPropertyChanged(nameof(IsHeightEditable));
        if (suppressRegenerate) return;

        if (isGridDirty && oldValue is not null)
        {
            _ = MaybeRegenerateWithConfirmationAsync(() =>
            {
                suppressRegenerate = true;
                try
                {
                    TemplateShape = oldValue;
                    OnPropertyChanged(nameof(IsHeightEditable));
                }
                finally
                {
                    suppressRegenerate = false;
                }
            });
            return;
        }

        RegenerateGrid();
    }

    partial void OnTemplateWidthChanged(int oldValue, int newValue)
    {
        if (suppressRegenerate) return;

        if (isGridDirty)
        {
            _ = MaybeRegenerateWithConfirmationAsync(() =>
            {
                suppressRegenerate = true;
                try
                {
                    TemplateWidth = oldValue;
                    if (IsHeightLocked) TemplateHeight = oldValue;
                }
                finally
                {
                    suppressRegenerate = false;
                }
            });
            return;
        }

        if (IsHeightLocked)
        {
            suppressRegenerate = true;
            TemplateHeight = newValue;
            suppressRegenerate = false;
        }

        RegenerateGrid();
    }

    partial void OnTemplateHeightChanged(int oldValue, int newValue)
    {
        if (suppressRegenerate) return;

        if (isGridDirty)
        {
            _ = MaybeRegenerateWithConfirmationAsync(() =>
            {
                suppressRegenerate = true;
                try
                {
                    TemplateHeight = oldValue;
                }
                finally
                {
                    suppressRegenerate = false;
                }
            });
            return;
        }

        RegenerateGrid();
    }

    partial void OnIsHeightLockedChanged(bool value)
    {
        if (value)
            TemplateHeight = TemplateWidth;

        OnPropertyChanged(nameof(IsHeightEditable));
    }

    partial void OnCanExportChanged(bool value)
    {
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTetrisTypeChanged(EnumDisplayItem<TetrisType> value)
    {
        SolverResult = null;
        CanExport = false;
        ClearStatus();
        RecomputeMaxPlacements();
    }

    partial void OnSelectedSymmetryTypeChanged(EnumDisplayItem<SymmetryType> value)
    {
        SolverResult = null;
        CanExport = false;
        ClearStatus();
        OnPropertyChanged(nameof(IsSymmetryEnabled));
        OnPropertyChanged(nameof(IsRotation90NonSquareWarning));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    partial void OnPaintModeChanged(PaintMode value)
    {
        OnPropertyChanged(nameof(IsPaintModeBlock));
        OnPropertyChanged(nameof(IsPaintModeClear));
        OnPropertyChanged(nameof(IsPaintModeToggle));
    }

    partial void OnUiScaleChanged(double value)
    {
        ScaleChanged?.Invoke(value);
    }

    partial void OnGridChanged(Grid value)
    {
        RecomputeMaxPlacements();
        OnPropertyChanged(nameof(IsRotation90NonSquareWarning));
        GenerateCommand.NotifyCanExecuteChanged();
    }

    partial void OnTargetPlacementCountChanged(int value)
    {
        if (MaxPlacements <= 0)
        {
            OnPropertyChanged(nameof(DensityDisplayText));
            OnPropertyChanged(nameof(IsMaximize));
            return;
        }

        var clamped = Math.Clamp(value, SliderMinimum, MaxPlacements);
        if (clamped != value)
        {
            TargetPlacementCount = clamped;
            return;
        }

        if (!suppressRatioUpdate)
        {
            targetRatio = (double)TargetPlacementCount / MaxPlacements;
            hasTargetRatio = true;
        }

        OnPropertyChanged(nameof(DensityDisplayText));
        OnPropertyChanged(nameof(IsMaximize));
    }

    partial void OnMaxPlacementsChanged(int value) =>
        OnPropertyChanged(nameof(SliderMaximum));

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalMinutes >= 1)
        {
            int mins = (int)elapsed.TotalMinutes;
            int secs = elapsed.Seconds;
            return $"{mins} min {secs} s";
        }
        if (elapsed.TotalSeconds >= 10)
            return $"{elapsed.TotalSeconds:F1} s";
        if (elapsed.TotalSeconds >= 1)
            return $"{elapsed.TotalSeconds:F2} s";
        return $"{elapsed.TotalMilliseconds:F0} ms";
    }
}
