using ApsGenerator.UI.Controls;
using ApsGenerator.UI.Services;
using ApsGenerator.UI.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ApsGenerator.UI;

public partial class MainWindow : Window
{
    private readonly GridCanvas? gridCanvas;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;

        gridCanvas = this.FindControl<GridCanvas>("GridCanvas");
        gridCanvas?.CellClicked += OnCellClicked;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.ConfirmAsync = ShowConfirmationAsync;
            vm.ShowExportDialogAsync = ShowExportDialogAsync;
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var dialog = new SettingsDialog(vm);
        await dialog.ShowDialog(this);
    }

    private async void OnUpdateIconClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.ShowPendingReleaseNotes is null)
            return;

        try
        {
            await vm.ShowPendingReleaseNotes();
        }
        catch (Exception ex)
        {
            var dialog = new ConfirmationDialog($"Update failed: {ex.Message}", vm.UiScale);
            await dialog.ShowDialog(this);
        }
    }

    private async Task<bool> ShowConfirmationAsync(string message)
    {
        var dialog = new ConfirmationDialog(message);
        var result = await dialog.ShowDialog<bool>(this);
        return result;
    }

    private async Task ShowExportDialogAsync()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (vm.SolverResult is null || vm.SolverResult.Placements.Count == 0)
            return;

        var tetrisType = vm.SelectedTetrisType.Value;
        var dialog = new ExportDialog(
            vm.SolverResult,
            vm.Grid,
            tetrisType,
            vm.LastExportFolder,
            vm.CoolerSession,
            vm.DefaultExportHeightBasic,
            vm.DefaultExportHeightFiveClip,
            vm.ExportNameTemplate,
            vm.ThreadCount,
            maxTimeSeconds: vm.MaxTimeSeconds,
            extraLayers: vm.ExportExtraLayersFor(tetrisType));

        var exported = await dialog.ShowDialog<bool>(this);
        if (exported)
        {
            if (dialog.ExportedFolder is string folder)
                vm.LastExportFolder = folder;

            vm.SetExportExtraLayersFor(tetrisType, dialog.ExportedExtraLayers);

            UserSettingsStore.Save(vm.CreateUserSettings());

            vm.StatusLabel = "Exported";
            vm.StatusDetailText = "";
        }
    }

    private void OnCellClicked(int row, int col)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.PaintCellCommand.Execute((row, col));

        gridCanvas?.NotifyGridChanged();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            // Order: cancel Tetris + cooler, persist settings, dispose cooler session.
            viewModel.CancelCommand.Execute(null);
            viewModel.DisposeCoolerSession();
            UserSettingsStore.Save(viewModel.CreateUserSettings());
        }
    }
}
