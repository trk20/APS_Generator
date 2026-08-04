using System.Globalization;
using ApsGenerator.Core.Models;
using ApsGenerator.Solver;
using ApsGenerator.UI.Services;
using ApsGenerator.UI.Services.Export;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace ApsGenerator.UI;

public partial class ExportDialog : Window
{
    private sealed record ExtraLayersItem(ExportExtraLayers Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record BomItem(string Label, string Cost);

    private IReadOnlyList<Placement> placements;
    private readonly Core.Models.Grid grid;
    private readonly TetrisType tetrisType;
    private readonly string exportNameTemplate;
    private readonly int threadCount;
    private readonly double maxTimeSeconds;
    private readonly CoolerSolveSession coolerSession;
    private readonly bool ownsCoolerSession;
    private readonly ExportExtraLayers initialExtraLayers;
    private CancellationTokenSource? dialogCts;
    private CancellationTokenSource? bomCts;
    private bool hasManualBlueprintNameEdit;
    private string? lastAutoBlueprintName;
    private int bomGeneration;
    private bool isExporting;
    private readonly bool supportsCoolerSnakes;

    public string? ExportedFolder { get; private set; }
    public ExportExtraLayers ExportedExtraLayers { get; private set; }

    public ExportDialog()
    {
        InitializeComponent();
        placements = Array.Empty<Placement>();
        grid = new ApsGenerator.Core.Models.Grid(1, 1);
        tetrisType = TetrisType.ThreeClip;
        exportNameTemplate = UserSettings.DefaultExportNameTemplate;
        threadCount = Math.Max(1, Environment.ProcessorCount - 1);
        maxTimeSeconds = CoolerSnakeOptions.DefaultMaxTimeSeconds;
        coolerSession = new CoolerSolveSession(new Solver.Cooler.CoolerSnakeSolver());
        ownsCoolerSession = true;
        supportsCoolerSnakes = true;
        initialExtraLayers = ExportExtraLayers.EjectorsIntakesCoolerSnake;
        ExportedExtraLayers = initialExtraLayers;
        dialogCts = new CancellationTokenSource();
        Closed += OnDialogClosed;
        InitializeDialog(lastExportFolder: null, defaultHeightBasic: 2, defaultHeightFiveClip: 3);
    }

    public ExportDialog(
        SolverResult result,
        ApsGenerator.Core.Models.Grid grid,
        TetrisType tetrisType,
        string? lastExportFolder,
        CoolerSolveSession coolerSession,
        int defaultHeightBasic = 2,
        int defaultHeightFiveClip = 3,
        string? exportNameTemplate = null,
        int? threadCount = null,
        double? maxTimeSeconds = null,
        ExportExtraLayers extraLayers = ExportExtraLayers.EjectorsIntakesCoolerSnake)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(coolerSession);

        this.placements = result.Placements;
        this.grid = grid;
        this.tetrisType = tetrisType;
        this.exportNameTemplate = exportNameTemplate ?? UserSettings.DefaultExportNameTemplate;
        this.threadCount = Math.Max(1, threadCount ?? Environment.ProcessorCount - 1);
        this.maxTimeSeconds = Math.Clamp(
            maxTimeSeconds ?? CoolerSnakeOptions.DefaultMaxTimeSeconds,
            1,
            CoolerSnakeOptions.DefaultMaxTimeSeconds);
        this.coolerSession = coolerSession;
        ownsCoolerSession = false;
        supportsCoolerSnakes = tetrisType.SupportsCoolerSnakes();
        initialExtraLayers = extraLayers.ClampFor(tetrisType);
        ExportedExtraLayers = initialExtraLayers;

        dialogCts = new CancellationTokenSource();
        Closed += OnDialogClosed;
        InitializeDialog(lastExportFolder, defaultHeightBasic, defaultHeightFiveClip);
    }

    private void OnDialogClosed(object? sender, EventArgs e)
    {
        bomCts?.Cancel();
        bomCts?.Dispose();
        bomCts = null;
        dialogCts?.Cancel();
        dialogCts?.Dispose();
        dialogCts = null;
        if (ownsCoolerSession)
            coolerSession.Dispose();
    }

    private void InitializeDialog(string? lastExportFolder, int defaultHeightBasic, int defaultHeightFiveClip)
    {
        ConfigureForTetrisType(tetrisType, defaultHeightBasic, defaultHeightFiveClip);
        AddHandler(
            KeyDownEvent,
            OnDialogKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        BlueprintNameBox.TextChanged += OnBlueprintNameTextChanged;
        TargetHeightBox.ValueChanged += OnTargetHeightValueChanged;
        ExtraLayersBox.SelectionChanged += OnExportOptionChanged;
        hasManualBlueprintNameEdit = false;
        UpdateAutoBlueprintName();
        SaveLocationBox.Text = ResolveDefaultFolder(lastExportFolder);
        _ = UpdateBillOfMaterialsAsync();
    }

    private void ConfigureForTetrisType(TetrisType type, int defaultHeightBasic, int defaultHeightFiveClip)
    {
        TargetHeightLabel.Text = type == TetrisType.FiveClip ? "Stack height" : "Clip/Loader length";

        var items = ExportExtraLayersExtensions.OptionsFor(type)
            .Select(v => new ExtraLayersItem(v, v.DisplayLabel(type)))
            .ToList();
        ExtraLayersBox.ItemsSource = items;
        ExtraLayersBox.SelectedItem = items.FirstOrDefault(i => i.Value == initialExtraLayers)
            ?? items[0];

        ToolTip.SetTip(
            ExtraLayersBox,
            type == TetrisType.FiveClip
                ? "Choose whether to include a cooler snake."
                : "Choose the configuration of extra layers to include.");

        if (type == TetrisType.FiveClip)
        {
            TargetHeightBox.Minimum = FiveClipHeight.MinHeight;
            TargetHeightBox.Maximum = FiveClipHeight.MaxHeight;
            TargetHeightBox.Increment = FiveClipHeight.Step;
            TargetHeightBox.Value = FiveClipHeight.RoundToMultipleOf3(defaultHeightFiveClip);
        }
        else
        {
            TargetHeightBox.Minimum = 1;
            TargetHeightBox.Maximum = 8;
            TargetHeightBox.Increment = 1;
            TargetHeightBox.Value = defaultHeightBasic;
        }
    }

    private ExportExtraLayers SelectedExtraLayers =>
        ExtraLayersBox.SelectedItem is ExtraLayersItem item
            ? item.Value
            : initialExtraLayers;

    private string GenerateDefaultName()
    {
        int clipCount = tetrisType.ClipCount();
        int targetHeight = (int)(TargetHeightBox.Value ?? (tetrisType == TetrisType.FiveClip ? 3 : 2));
        return exportNameTemplate
            .Replace("{width}", grid.Width.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{height}", grid.Height.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{clips}", clipCount.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{count}", placements.Count.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{targetHeight}", targetHeight.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateAutoBlueprintName()
    {
        if (hasManualBlueprintNameEdit)
            return;

        string autoName = GenerateDefaultName();
        lastAutoBlueprintName = autoName;
        BlueprintNameBox.Text = autoName;
    }

    private void OnTargetHeightValueChanged(object? sender, EventArgs e)
    {
        ClampFiveClipTargetHeight();
        _ = UpdateBillOfMaterialsAsync();

        if (hasManualBlueprintNameEdit)
            return;

        UpdateAutoBlueprintName();
    }

    private void ClampFiveClipTargetHeight()
    {
        if (tetrisType != TetrisType.FiveClip)
            return;

        int currentValue = (int)(TargetHeightBox.Value ?? FiveClipHeight.MinHeight);
        int clampedValue = FiveClipHeight.RoundToMultipleOf3(currentValue);
        if (currentValue == clampedValue)
            return;

        TargetHeightBox.Value = clampedValue;
    }

    private void OnBlueprintNameTextChanged(object? sender, EventArgs e)
    {
        string currentName = BlueprintNameBox.Text ?? string.Empty;
        if (string.Equals(currentName, lastAutoBlueprintName, StringComparison.Ordinal))
            return;

        hasManualBlueprintNameEdit = true;
    }

    private void OnExportOptionChanged(object? sender, EventArgs e)
    {
        _ = UpdateBillOfMaterialsAsync();
    }

    private async Task UpdateBillOfMaterialsAsync()
    {
        int generation = ++bomGeneration;
        int targetHeight = (int)(TargetHeightBox.Value ?? 2);
        var extraLayers = SelectedExtraLayers;

        bomCts?.Cancel();
        bomCts?.Dispose();
        bomCts = CancellationTokenSource.CreateLinkedTokenSource(
            dialogCts?.Token ?? CancellationToken.None);
        var ct = bomCts.Token;

        SetCoolerBusy(NeedsCoolerSolve(extraLayers) && coolerSession.NeedsFreshExportSolve(extraLayers));

        try
        {
            var built = await ResolveCoolerAndBuildAsync(
                BlueprintNameBox.Text ?? string.Empty,
                targetHeight,
                extraLayers,
                ct).ConfigureAwait(true);
            if (generation != bomGeneration || built is null)
                return;

            if (built.Value.Cooler is { Status: CoolerSnakeStatus.Error } err)
            {
                BomList.ItemsSource = null;
                TotalCostText.Text = "unavailable";
                ShowError($"Cooler snake error: {err.Detail}");
                return;
            }

            ApplyBillOfMaterials(built.Value.Blueprint);
        }
        catch (OperationCanceledException)
        {
            // Superseded BOM update or dialog closed.
        }
        catch (Exception ex)
        {
            if (generation != bomGeneration)
                return;
            BomList.ItemsSource = null;
            TotalCostText.Text = "unavailable";
            ShowError($"Bill of materials failed: {ex.Message}");
        }
        finally
        {
            if (generation == bomGeneration)
                SetCoolerBusy(false);
        }
    }

    private void ApplyBillOfMaterials(BlueprintFile previewBlueprint)
    {
        var idToBlock = new Dictionary<int, BlockDefinition>();
        foreach (var (_, definition) in GameData.Blocks)
            idToBlock[definition.BlockId] = definition;

        var counts = previewBlueprint.Blueprint.BlockIds
            .GroupBy(id => id)
            .Select(group =>
            {
                int count = group.Count();
                if (!idToBlock.TryGetValue(group.Key, out var definition))
                {
                    return new
                    {
                        Name = $"Unknown ({group.Key})",
                        Count = count,
                        TotalCost = 0L
                    };
                }

                return new
                {
                    Name = FormatBlockName(definition.Name),
                    Count = count,
                    TotalCost = (long)count * definition.MaterialCost
                };
            })
            .GroupBy(item => item.Name)
            .Select(group => new
            {
                Name = group.Key,
                Count = group.Sum(item => item.Count),
                TotalCost = group.Sum(item => item.TotalCost)
            })
            .OrderByDescending(item => item.TotalCost)
            .ToList();

        BomList.ItemsSource = counts
            .Select(item =>
                new BomItem(
                    $"{item.Name} × {item.Count.ToString("N0", CultureInfo.InvariantCulture)}",
                    item.TotalCost.ToString("N0", CultureInfo.InvariantCulture)))
            .ToList();

        long totalCost = Convert.ToInt64(Math.Round(previewBlueprint.SavedMaterialCost));
        TotalCostText.Text = totalCost.ToString("N0", CultureInfo.InvariantCulture);
    }

    private void SetCoolerBusy(bool busy)
    {
        CoolerProgress.IsVisible = busy;
        ExportButton.IsEnabled = !busy && !isExporting;
    }

    private static string FormatBlockName(string blockKey)
    {
        if (blockKey.StartsWith("Cooler", StringComparison.Ordinal))
            return "Cooler";

        int underscoreIndex = blockKey.LastIndexOf('_');
        if (underscoreIndex < 0)
            return blockKey;

        string baseName = blockKey[..underscoreIndex];
        string suffix = blockKey[(underscoreIndex + 1)..];

        var readable = new System.Text.StringBuilder();
        foreach (char c in baseName)
        {
            if (char.IsUpper(c) && readable.Length > 0)
                readable.Append(' ');
            readable.Append(c);
        }

        if (baseName is "Loader" or "Clip")
            return $"{readable} ({suffix}m)";

        return readable.ToString();
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close(false);
    }

    private static string ResolveDefaultFolder(string? lastExportFolder)
    {
        if (!string.IsNullOrWhiteSpace(lastExportFolder) && Directory.Exists(lastExportFolder))
            return lastExportFolder;

        var detected = BlueprintPathResolver.Resolve();
        if (detected is not null)
            return detected;

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
            return documents;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            return Path.Combine(userProfile, "Documents");

        return Environment.CurrentDirectory;
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        IStorageFolder? suggestedStartLocation = null;
        var saveLocation = SaveLocationBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(saveLocation))
            suggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(saveLocation);

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select export folder",
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStartLocation,
        });

        if (folders.Count > 0)
            SaveLocationBox.Text = folders[0].Path.LocalPath;
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        ClearError();
        if (!TryValidateExportInputs(out var name, out var targetHeight, out var extraLayers, out var saveLocation))
            return;

        var filePath = Path.Combine(saveLocation, name + ".blueprint");
        if (File.Exists(filePath) && !await ConfirmOverwriteAsync().ConfigureAwait(true))
            return;

        isExporting = true;
        ExportButton.IsEnabled = false;
        SetCoolerBusy(NeedsCoolerSolve(extraLayers) && coolerSession.NeedsFreshExportSolve(extraLayers));
        var ct = dialogCts?.Token ?? CancellationToken.None;

        try
        {
            if (!await TryExportBlueprintAsync(name, targetHeight, extraLayers, filePath, ct)
                .ConfigureAwait(true))
                return;

            ExportedFolder = saveLocation;
            ExportedExtraLayers = extraLayers;
            Tag = saveLocation;
            Close(true);
        }
        catch (OperationCanceledException)
        {
            ShowError("Export cancelled.");
        }
        catch (Exception ex)
        {
            ShowError($"Export failed: {ex.Message}");
        }
        finally
        {
            isExporting = false;
            SetCoolerBusy(false);
            ExportButton.IsEnabled = true;
        }
    }

    private bool TryValidateExportInputs(
        out string name,
        out int targetHeight,
        out ExportExtraLayers extraLayers,
        out string saveLocation)
    {
        name = BlueprintNameBox.Text?.Trim() ?? "";
        targetHeight = (int)(TargetHeightBox.Value ?? 2);
        extraLayers = SelectedExtraLayers;
        saveLocation = "";

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Blueprint name is required.");
            return false;
        }

        if (tetrisType == TetrisType.FiveClip)
        {
            targetHeight = FiveClipHeight.RoundToMultipleOf3(targetHeight);
            TargetHeightBox.Value = targetHeight;
        }

        if (ExtraLayersBox.SelectedItem is not ExtraLayersItem)
        {
            ShowError("Extra layers selection is required.");
            return false;
        }

        saveLocation = SaveLocationBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(saveLocation))
        {
            ShowError("Save location is required.");
            return false;
        }

        if (!Directory.Exists(saveLocation))
        {
            ShowError("Save location does not exist.");
            return false;
        }

        return true;
    }

    private async Task<bool> ConfirmOverwriteAsync()
    {
        var uiScale = (RootTransform.LayoutTransform as ScaleTransform)?.ScaleX ?? 1.0;
        var dialog = new ConfirmationDialog("Overwrite existing blueprint file?", uiScale);
        return await dialog.ShowDialog<bool>(this);
    }

    private async Task<bool> TryExportBlueprintAsync(
        string name,
        int targetHeight,
        ExportExtraLayers extraLayers,
        string filePath,
        CancellationToken ct)
    {
        var built = await ResolveCoolerAndBuildAsync(
            name, targetHeight, extraLayers, ct).ConfigureAwait(true);
        if (built is null)
            return false;

        if (built.Value.Cooler is { Status: CoolerSnakeStatus.Error } err)
        {
            ShowError($"Cooler snake error: {err.Detail}");
            return false;
        }

        if (built.Value.Cooler is { Status: not CoolerSnakeStatus.Sat } cooler
            && NeedsCoolerSolve(extraLayers))
        {
            ShowError($"Cooler snake solve failed ({cooler.Status}): {cooler.Detail}");
            return false;
        }

        BlueprintExporter.Export(
            built.Value.Placements, grid, tetrisType, built.Value.Options, filePath);
        return true;
    }

    private bool NeedsCoolerSolve(ExportExtraLayers extraLayers) =>
        supportsCoolerSnakes
        && placements.Count > 0
        && extraLayers.NeedsCoolerSolve(tetrisType);

    private async Task<(
        CoolerSnakeResult? Cooler,
        IReadOnlyList<Placement> Placements,
        ExportOptions Options,
        BlueprintFile Blueprint)?> ResolveCoolerAndBuildAsync(
        string blueprintName,
        int targetHeight,
        ExportExtraLayers extraLayers,
        CancellationToken cancellationToken)
    {
        CoolerSnakeResult? cooler = await SolveCoolersAsync(extraLayers, cancellationToken)
            .ConfigureAwait(true);

        var options = new ExportOptions(
            blueprintName,
            targetHeight,
            extraLayers,
            NeedsCoolerSolve(extraLayers) ? cooler : null);
        var exportPlacements = placements;
        var blueprint = BlueprintBuilder.Build(exportPlacements, grid, tetrisType, options);
        return (cooler, exportPlacements, options, blueprint);
    }

    private async Task<CoolerSnakeResult?> SolveCoolersAsync(
        ExportExtraLayers extraLayers,
        CancellationToken cancellationToken)
    {
        if (!NeedsCoolerSolve(extraLayers))
            return null;

        var request = new CoolerExportSolveRequest(
            grid,
            tetrisType,
            placements,
            threadCount,
            maxTimeSeconds,
            OmitEjectors: extraLayers.OmitEjectorsForCoolerSolve());

        return await coolerSession.SolveForExportAsync(request, cancellationToken).ConfigureAwait(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void ClearError() => ErrorText.IsVisible = false;
}
