using System;
using System.Threading.Tasks;
using ApsGenerator.UI.Services;
using ApsGenerator.UI.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ApsGenerator.UI;

public partial class SettingsDialog : Window
{
    private readonly MainWindowViewModel viewModel;

    public SettingsDialog()
    {
        InitializeComponent();
        RegisterDialogHandlers();
        viewModel = new MainWindowViewModel();
        SetVersionLabel();
    }

    public SettingsDialog(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        RegisterDialogHandlers();
        this.viewModel = viewModel;
        DataContext = viewModel;
        SetVersionLabel();
    }

    private void SetVersionLabel()
    {
        VersionLabel.Text = $"Version: {UpdateService.GetCurrentVersion() ?? "dev"}";
    }

    private void RegisterDialogHandlers()
    {
        AddHandler(
            KeyDownEvent,
            OnDialogKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Opened += (_, _) => RootPanel.Focus();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        viewModel.DefaultExportHeightFiveClip =
            FiveClipHeight.RoundToMultipleOf3(viewModel.DefaultExportHeightFiveClip);
        UserSettingsStore.Save(viewModel.CreateUserSettings());
        Close();
    }

    private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e)
    {
        CheckForUpdatesButton.IsEnabled = false;
        CheckForUpdatesButton.Content = "Checking...";

        try
        {
            var mgr = UpdateService.CreateUpdateManager(viewModel.ReceiveExperimentalUpdates);
            var updateInfo = await mgr.CheckForUpdatesAsync();

            if (updateInfo is null)
            {
                var upToDate = new ConfirmationDialog("You're up to date!", viewModel.UiScale);
                await upToDate.ShowDialog(this);
                return;
            }

            var targetVersion = updateInfo.TargetFullRelease.Version.ToString();
            var rawNotes = updateInfo.TargetFullRelease.NotesMarkdown;
            var processedNotes = UpdateService.ProcessReleaseNotes(rawNotes, targetVersion);

            var dialog = new ReleaseNotesDialog(targetVersion, processedNotes, showUpdate: true);
            var shouldUpdate = await dialog.ShowDialog<bool>(this);

            if (shouldUpdate)
            {
                CheckForUpdatesButton.Content = "Downloading...";
                await mgr.DownloadUpdatesAsync(updateInfo);
                viewModel.PendingReleaseNotesVersion = targetVersion;
                viewModel.PendingReleaseNotesContent = rawNotes;
                UserSettingsStore.Save(viewModel.CreateUserSettings());
                mgr.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
            }
            else
            {
                viewModel.UpdateAvailable = true;
                viewModel.UpdateVersionText = $"v{targetVersion}";
                viewModel.LastSeenUpdateVersion = targetVersion;
                UserSettingsStore.Save(viewModel.CreateUserSettings());
            }
        }
        catch (Exception ex)
        {
            var errorDialog = new ConfirmationDialog($"Update check failed: {ex.Message}", viewModel.UiScale);
            await errorDialog.ShowDialog(this);
        }
        finally
        {
            CheckForUpdatesButton.IsEnabled = true;
            CheckForUpdatesButton.Content = "Check for Updates";
        }
    }

    private void OnDefaultExportHeightFiveClipValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (sender is not NumericUpDown numericUpDown)
            return;

        int currentValue = (int)(numericUpDown.Value ?? FiveClipHeight.MinHeight);
        int roundedValue = FiveClipHeight.RoundToMultipleOf3(currentValue);
        if (currentValue == roundedValue)
            return;

        numericUpDown.Value = roundedValue;
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close();
    }
}
