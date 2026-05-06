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
        if (gridCanvas is not null)
            gridCanvas.CellClicked += OnCellClicked;
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

        var dialog = new ExportDialog(
            vm.SolverResult,
            vm.Grid,
            vm.SelectedTetrisType.Value,
            vm.LastExportFolder,
            vm.DefaultExportHeightBasic,
            vm.DefaultExportHeightFiveClip,
            vm.ExportNameTemplate);

        var exported = await dialog.ShowDialog<bool>(this);
        if (exported)
        {
            if (dialog.Tag is string folder)
            {
                vm.LastExportFolder = folder;
                UserSettingsStore.Save(vm.CreateUserSettings());
            }

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
            // Order: cancel in-flight solve, then persist settings, then close.
            viewModel.CancelCommand.Execute(null);
            UserSettingsStore.Save(viewModel.CreateUserSettings());
        }
    }
}
