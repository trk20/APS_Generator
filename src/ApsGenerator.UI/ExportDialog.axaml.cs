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
    private readonly IReadOnlyList<Placement> placements;
    private readonly ApsGenerator.Core.Models.Grid grid;
    private readonly TetrisType tetrisType;
    private readonly string exportNameTemplate;
    private bool hasManualBlueprintNameEdit;
    private string? lastAutoBlueprintName;

    private sealed record BomItem(string Label, string Cost);

    public ExportDialog()
    {
        InitializeComponent();
        placements = Array.Empty<Placement>();
        grid = new ApsGenerator.Core.Models.Grid(1, 1);
        tetrisType = TetrisType.ThreeClip;
        exportNameTemplate = UserSettings.DefaultExportNameTemplate;
        InitializeDialog(lastExportFolder: null, defaultHeightBasic: 2, defaultHeightFiveClip: 3);
    }

    public ExportDialog(
        SolverResult result,
        ApsGenerator.Core.Models.Grid grid,
        TetrisType tetrisType,
        string? lastExportFolder,
        int defaultHeightBasic = 2,
        int defaultHeightFiveClip = 3,
        string? exportNameTemplate = null)
    {
        InitializeComponent();

        this.placements = result.Placements;
        this.grid = grid;
        this.tetrisType = tetrisType;
        this.exportNameTemplate = exportNameTemplate ?? UserSettings.DefaultExportNameTemplate;

        InitializeDialog(lastExportFolder, defaultHeightBasic, defaultHeightFiveClip);
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
        hasManualBlueprintNameEdit = false;
        UpdateAutoBlueprintName();
        SaveLocationBox.Text = ResolveDefaultFolder(lastExportFolder);
        UpdateBillOfMaterials();
    }

    private void ConfigureForTetrisType(TetrisType type, int defaultHeightBasic, int defaultHeightFiveClip)
    {
        TargetHeightLabel.Text = type == TetrisType.FiveClip ? "Stack height" : "Clip/Loader length";

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

    private string GenerateDefaultName()
    {
        int clipCount = tetrisType switch
        {
            TetrisType.ThreeClip => 3,
            TetrisType.FourClip => 4,
            TetrisType.FiveClip => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(tetrisType), tetrisType, "Unsupported tetris type.")
        };

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
        UpdateBillOfMaterials();

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

    private void UpdateBillOfMaterials()
    {
        int targetHeight = (int)(TargetHeightBox.Value ?? 2);

        try
        {
            var previewOptions = new ExportOptions(BlueprintNameBox.Text ?? string.Empty, targetHeight);
            BlueprintFile previewBlueprint = BlueprintBuilder.Build(placements, grid, tetrisType, previewOptions);

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
                .OrderByDescending(item => item.TotalCost)
                .ToList();

            var bomItems = counts
                .Select(item =>
                    new BomItem(
                        $"{item.Name} × {item.Count.ToString("N0", CultureInfo.InvariantCulture)}",
                        item.TotalCost.ToString("N0", CultureInfo.InvariantCulture)))
                .ToList();

            BomList.ItemsSource = bomItems;

            long totalCost = Convert.ToInt64(Math.Round(previewBlueprint.SavedMaterialCost));
            TotalCostText.Text = totalCost.ToString("N0", CultureInfo.InvariantCulture);
        }
        catch
        {
            BomList.ItemsSource = null;
            TotalCostText.Text = "unavailable";
        }
    }

    private static string FormatBlockName(string blockKey)
    {
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

        var name = BlueprintNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Blueprint name is required.");
            return;
        }

        var targetHeight = (int)(TargetHeightBox.Value ?? 2);
        if (tetrisType == TetrisType.FiveClip)
        {
            targetHeight = FiveClipHeight.RoundToMultipleOf3(targetHeight);
            TargetHeightBox.Value = targetHeight;
        }

        var saveLocation = SaveLocationBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(saveLocation))
        {
            ShowError("Save location is required.");
            return;
        }

        if (!Directory.Exists(saveLocation))
        {
            ShowError("Save location does not exist.");
            return;
        }

        var filePath = Path.Combine(saveLocation, name + ".blueprint");

        if (File.Exists(filePath))
        {
            var uiScale = (RootTransform.LayoutTransform as ScaleTransform)?.ScaleX ?? 1.0;
            var dialog = new ConfirmationDialog("Overwrite existing blueprint file?", uiScale);
            var confirmed = await dialog.ShowDialog<bool>(this);
            if (!confirmed)
                return;
        }

        try
        {
            var options = new ExportOptions(name, targetHeight);
            BlueprintExporter.Export(placements, grid, tetrisType, options, filePath);
            Tag = saveLocation;
            Close(true);
        }
        catch (Exception ex)
        {
            ShowError($"Export failed: {ex.Message}");
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void ClearError() => ErrorText.IsVisible = false;
}
