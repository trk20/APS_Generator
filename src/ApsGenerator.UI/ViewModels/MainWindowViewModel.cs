using ApsGenerator.Core;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver;
using ApsGenerator.Solver.Cooler;
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
    private bool autoUpdate = true;
    [ObservableProperty]
    private bool receiveExperimentalUpdates;
    [ObservableProperty]
    private bool showReleaseNotesAfterUpdate = true;
    [ObservableProperty]
    private bool updateAvailable;
    [ObservableProperty]
    private string updateVersionText = "";

    public string? PendingReleaseNotesVersion { get; set; }
    public string? PendingReleaseNotesContent { get; set; }
    public string? LastSeenUpdateVersion { get; set; }

    [ObservableProperty]
    private bool earlyStopEnabled = true;
    [ObservableProperty]
    private bool generateCoolerSnake = true;
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

    private int displayedPlacementCount = -1;

    public string PlacedText =>
        displayedPlacementCount < 0 ? "" : $"{displayedPlacementCount} / {MaxPlacements}";

    public string EffectiveAutoloadersText =>
        displayedPlacementCount < 0
            ? ""
            : (displayedPlacementCount * SelectedTetrisType.Value.EffectiveAutoloadersPerPlacement()).ToString();

    [ObservableProperty]
    private bool hasSolverRun;

    /// <summary>Status panel shows during an active solve (wall-clock) and after.</summary>
    public bool ShowStatusSection => HasSolverRun || IsGenerating;

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

    [ObservableProperty]
    private bool showCoolerOverlay = true;

    /// <summary>Tetris types that support cooler-snake generation.</summary>
    public bool SupportsCoolerSnakes => SelectedTetrisType.Value.SupportsCoolerSnakes();

    /// <summary>Visual overlay toggle — only when a cooler solve is applicable / in progress.</summary>
    public bool CanToggleCoolerOverlay =>
        GenerateCoolerSnake
        && SupportsCoolerSnakes
        && HasSolverRun
        && SolverResult is { Placements.Count: > 0 };

    public CoolerSnakeResult? CoolerResult => coolerSession.Result;
    public bool IsCoolerBusy => coolerSession.IsBusy;

    public ExportExtraLayers ExportExtraLayersFor(TetrisType type) =>
        type == TetrisType.FiveClip
            ? ExportExtraLayersFiveClip.ClampFor(TetrisType.FiveClip)
            : ExportExtraLayersBasic;

    public void SetExportExtraLayersFor(TetrisType type, ExportExtraLayers layers)
    {
        if (type == TetrisType.FiveClip)
            ExportExtraLayersFiveClip = layers.ClampFor(TetrisType.FiveClip);
        else
            ExportExtraLayersBasic = layers;
    }

    /// <summary>Shared cooler solve session (overlay + export cache).</summary>
    public CoolerSolveSession CoolerSession => coolerSession;

    private readonly CoolerSolveSession coolerSession;

    partial void OnSolverResultChanged(SolverResult? value)
    {
        if (value is null)
        {
            allTrimmedSolutions = [];
            CurrentSolutionIndex = 0;
            OnPropertyChanged(nameof(HasMultipleSolutions));
            OnPropertyChanged(nameof(SolutionCounterText));
        }

        coolerSession.ClearResult();
        OnPropertyChanged(nameof(CoolerResult));
        NotifyCoolerAvailabilityChanged();
        coolerSession.ScheduleOverlaySolve();
    }

    [ObservableProperty]
    private int threadCount = Math.Max(1, Environment.ProcessorCount - 1);
    [ObservableProperty]
    private int defaultExportHeightBasic = 2;
    [ObservableProperty]
    private int defaultExportHeightFiveClip = 3;
    [ObservableProperty]
    private ExportExtraLayers exportExtraLayersBasic = ExportExtraLayers.EjectorsIntakesCoolerSnake;
    [ObservableProperty]
    private ExportExtraLayers exportExtraLayersFiveClip = ExportExtraLayers.EjectorsIntakesCoolerSnake;
    [ObservableProperty]
    private string exportNameTemplate = UserSettings.DefaultExportNameTemplate;

    public int MaxThreadCount => Math.Max(1, Environment.ProcessorCount - 1);

    public Func<string, Task<bool>>? ConfirmAsync { get; set; }
    public Func<Task>? ShowExportDialogAsync { get; set; }
    public Func<Task>? ShowPendingReleaseNotes { get; set; }
    public Func<Task>? ApplyPendingUpdate { get; set; }
    public Action<double>? ScaleChanged { get; set; }

    public int SliderMaximum => MaxPlacements;
    public int SliderMinimum => MaxPlacements / 3;
    public bool IsHeightEditable => TemplateShape.Value == UiTemplateShape.Rectangle && !IsHeightLocked;
    public bool IsLockButtonEnabled => TemplateShape.Value == UiTemplateShape.Rectangle;
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
        : this(new CoolerSnakeSolver())
    {
    }

    public MainWindowViewModel(CoolerSnakeSolver coolerSolver)
    {
        ArgumentNullException.ThrowIfNull(coolerSolver);
        coolerSession = new CoolerSolveSession(coolerSolver);
        coolerSession.StateChanged += OnCoolerSessionStateChanged;
        coolerSession.ConfigureOverlay(BuildOverlayRequest);

        suppressRegenerate = true;
        ApplyUserSettings(UserSettingsStore.Load());
        suppressRegenerate = false;
        RegenerateGrid();
    }

    private CoolerOverlaySolveRequest? BuildOverlayRequest()
    {
        if (!GenerateCoolerSnake
            || !SupportsCoolerSnakes
            || SolverResult is not { Placements.Count: > 0 })
            return null;

        return new CoolerOverlaySolveRequest(
            Grid,
            SelectedTetrisType.Value,
            SolverResult.Placements,
            Math.Max(1, ThreadCount),
            Math.Clamp(MaxTimeSeconds, 1, CoolerSnakeOptions.DefaultMaxTimeSeconds));
    }

    private void OnCoolerSessionStateChanged()
    {
        OnPropertyChanged(nameof(CoolerResult));
        OnPropertyChanged(nameof(IsCoolerBusy));
        NotifyCoolerAvailabilityChanged();

        if (coolerSession.IsBusy)
            return;

        if (CoolerResult is { Status: not CoolerSnakeStatus.Sat } failed)
            StatusDetailText = $"Cooler: {failed.Status} — {failed.Detail}";
    }

    public void DisposeCoolerSession()
    {
        coolerSession.StateChanged -= OnCoolerSessionStateChanged;
        coolerSession.Dispose();
    }

    public UserSettings CreateUserSettings() => UserSettingsResolution.FromViewModel(this);

    partial void OnDefaultExportHeightFiveClipChanged(int value)
    {
        int clampedValue = FiveClipHeight.RoundToMultipleOf3(value);
        if (value == clampedValue)
            return;

        DefaultExportHeightFiveClip = clampedValue;
    }

    private void ApplyUserSettings(UserSettings settings)
    {
        var validated = UserSettingsResolution.ApplyTo(this, settings, MaxThreadCount);
        applyMaximizeFromSettings = validated.IsMaximize;
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
        ClearPlacementDisplay();
        HasSolverRun = false;
        StatusLabel = "Ready";
        ElapsedTimeText = "";
        StatusDetailText = "";
        StatusForeground = DefaultStatusBrush;
        NotifyCoolerAvailabilityChanged();
        // SolverResult may already be null (no OnSolverResultChanged); still drop stale cooler cache.
        coolerSession.ScheduleOverlaySolve();
    }

    private void ClearPlacementDisplay()
    {
        displayedPlacementCount = -1;
        OnPropertyChanged(nameof(PlacedText));
        OnPropertyChanged(nameof(EffectiveAutoloadersText));
    }

    private void UpdatePlacementDisplay(int placementCount)
    {
        displayedPlacementCount = placementCount;
        OnPropertyChanged(nameof(PlacedText));
        OnPropertyChanged(nameof(EffectiveAutoloadersText));
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
        int newMaxPlacements = PlacementTargeting.TheoreticalMaxClusters(
            SelectedTetrisType.Value, Grid.AvailableCellCount);
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
        int clampedTarget = PlacementTargeting.FromRatio(targetRatio, MaxPlacements);

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

        CellState newState = CellPainting.NextState(Grid[cell.Row, cell.Col], PaintMode);
        var symmetryType = SelectedSymmetryType.Value;
        if (symmetryType == SymmetryType.Rotation90 && Grid.Width != Grid.Height)
        {
            StatusLabel = "Rotation90 requires square grid";
            StatusForeground = TimedOutBrush;
        }

        foreach (var (row, col) in CellPainting.PositionsToPaint(
                     cell.Row, cell.Col, Grid.Width, Grid.Height, symmetryType))
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
        BeginGenerateUi(out var ct);
        try
        {
            var options = BuildSolverOptions();
            var gridSnapshot = Grid.Clone();
            var tetrisType = SelectedTetrisType.Value;
            var result = await Task.Run(
                () => new TetrisSolver().Solve(gridSnapshot, tetrisType, options, ct), ct);

            result = ApplyTrimmedSolutionsIfNeeded(result, gridSnapshot, tetrisType);
            ApplySuccessfulSolve(result);
        }
        catch (OperationCanceledException)
        {
            StatusLabel = "Cancelled";
            ElapsedTimeText = FormatDuration(solveStopwatch!.Elapsed);
            StatusForeground = DefaultStatusBrush;
        }
        catch (Exception ex)
        {
            StatusLabel = "Error";
            StatusDetailText = ex.Message;
            ElapsedTimeText = FormatDuration(solveStopwatch!.Elapsed);
            StatusForeground = ErrorBrush;
        }
        finally
        {
            EndGenerateUi();
        }
    }

    private void BeginGenerateUi(out CancellationToken ct)
    {
        IsGenerating = true;
        ClearPlacementDisplay();
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
        ct = cancellationTokenSource.Token;

        solveStopwatch = System.Diagnostics.Stopwatch.StartNew();
        elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        elapsedTimer.Tick += (_, _) => ElapsedTimeText = FormatDuration(solveStopwatch?.Elapsed ?? TimeSpan.Zero);
        elapsedTimer.Start();
    }

    private void EndGenerateUi()
    {
        elapsedTimer?.Stop();
        elapsedTimer = null;
        solveStopwatch?.Stop();
        solveStopwatch = null;
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
        IsGenerating = false;
        HasSolverRun = true;
        NotifyCoolerAvailabilityChanged();
    }

    private SolverOptions BuildSolverOptions() => new()
    {
        MaxThreads = ThreadCount,
        MaxTimeSeconds = MaxTimeSeconds,
        SymmetryType = SelectedSymmetryType.Value,
        SymmetryMode = IsHardSymmetry ? SymmetryMode.Hard : SymmetryMode.Soft,
        EarlyStopEnabled = EarlyStopEnabled,
        TargetClusterCount = IsMaximize ? null : TargetPlacementCount,
        NumSolutions = NumSolutions
    };

    private SolverResult ApplyTrimmedSolutionsIfNeeded(
        SolverResult result,
        ApsGenerator.Core.Models.Grid gridSnapshot,
        TetrisType tetrisType)
    {
        if (IsMaximize || result.Placements.Count == 0)
            return result;

        var trimmedSolutions = new List<IReadOnlyList<Placement>>();
        foreach (var solution in result.AllSolutions)
        {
            var trimmed = PlacementTrimmer.Trim(
                solution, gridSnapshot, tetrisType,
                SelectedSymmetryType.Value, TargetPlacementCount);
            trimmedSolutions.Add(trimmed);
        }

        var firstTrimmed = trimmedSolutions[0];
        return new SolverResult
        {
            Placements = firstTrimmed,
            AllSolutions = trimmedSolutions,
            EmptyCells = CountEmptyCells(firstTrimmed, tetrisType, gridSnapshot.AvailableCellCount),
            Status = result.Status
        };
    }

    private void ApplySuccessfulSolve(SolverResult result)
    {
        allTrimmedSolutions = result.AllSolutions;
        CurrentSolutionIndex = 0;
        OnPropertyChanged(nameof(HasMultipleSolutions));
        OnPropertyChanged(nameof(SolutionCounterText));
        NextSolutionCommand.NotifyCanExecuteChanged();
        PrevSolutionCommand.NotifyCanExecuteChanged();

        SolverResult = result;
        CanExport = result.Placements.Count > 0;
        UpdatePlacementDisplay(result.ClusterCount);
        StatusLabel = result.Status switch
        {
            SolverStatus.Optimal => "Optimal",
            SolverStatus.LikelyOptimal => "Likely Optimal",
            SolverStatus.TargetDensityReached => "Target Density Reached",
            SolverStatus.TimedOut => "Timed Out",
            _ => result.Status.ToString()
        };
        ElapsedTimeText = FormatDuration(solveStopwatch!.Elapsed);
        StatusForeground = GetStatusBrush(result.Status);
    }

    private bool CanGenerate() => !IsGenerating && !IsRotation90NonSquareWarning;

    private static IBrush GetStatusBrush(SolverStatus status) => status switch
    {
        SolverStatus.Optimal => OptimalBrush,
        SolverStatus.LikelyOptimal => LikelyOptimalBrush,
        SolverStatus.TargetDensityReached => LikelyOptimalBrush,
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

    [RelayCommand(CanExecute = nameof(CanToggleCoolerOverlay))]
    private void ToggleCoolerOverlay()
    {
        ShowCoolerOverlay = !ShowCoolerOverlay;
    }

    partial void OnGenerateCoolerSnakeChanged(bool value)
    {
        if (!value)
            coolerSession.ClearResult();

        NotifyCoolerAvailabilityChanged();
        coolerSession.ScheduleOverlaySolve();
    }

    private void NotifyCoolerAvailabilityChanged()
    {
        OnPropertyChanged(nameof(SupportsCoolerSnakes));
        OnPropertyChanged(nameof(CanToggleCoolerOverlay));
        ToggleCoolerOverlayCommand.NotifyCanExecuteChanged();
    }

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

        SolverResult = new SolverResult
        {
            Placements = placements,
            AllSolutions = allTrimmedSolutions,
            EmptyCells = CountEmptyCells(placements, SelectedTetrisType.Value, Grid.AvailableCellCount),
            Status = SolverResult.Status
        };

        CanExport = placements.Count > 0;
        UpdatePlacementDisplay(placements.Count);
    }

    partial void OnIsGeneratingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStatusSection));
        GenerateCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ResetToTemplateCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasSolverRunChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStatusSection));
        NotifyCoolerAvailabilityChanged();
    }

    partial void OnTemplateShapeChanged(
        EnumDisplayItem<UiTemplateShape>? oldValue, EnumDisplayItem<UiTemplateShape> newValue)
    {
        OnPropertyChanged(nameof(IsHeightEditable));
        OnPropertyChanged(nameof(IsLockButtonEnabled));
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
                    OnPropertyChanged(nameof(IsLockButtonEnabled));
                }
                finally
                {
                    suppressRegenerate = false;
                }
            });
            return;
        }

        if (newValue.Value != UiTemplateShape.Rectangle)
        {
            IsHeightLocked = true;
            if (TemplateHeight != TemplateWidth)
            {
                suppressRegenerate = true;
                TemplateHeight = TemplateWidth;
                suppressRegenerate = false;
            }
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
        NotifyCoolerAvailabilityChanged();
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

    private static int CountEmptyCells(
        IReadOnlyList<Placement> placements,
        TetrisType tetrisType,
        int availableCellCount) =>
        PlacementCoverage.EmptyExclusiveCellCount(
            placements, ClusterShape.GetShapes(tetrisType), availableCellCount);
}
