using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ApsGenerator.UI.Services;
using ApsGenerator.UI.ViewModels;

namespace ApsGenerator.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            var mainWindow = new MainWindow { DataContext = vm };
            desktop.MainWindow = mainWindow;

            _ = CheckForUpdatesOnStartup(mainWindow, vm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task CheckForUpdatesOnStartup(Window owner, MainWindowViewModel vm)
    {
        try
        {
            var mgr = UpdateService.CreateUpdateManager(vm.ReceiveExperimentalUpdates);
            var updateInfo = await mgr.CheckForUpdatesAsync();
            if (updateInfo is null)
                return;

            var targetVersion = updateInfo.TargetFullRelease.Version.ToString();
            var releaseNotes = updateInfo.TargetFullRelease.NotesMarkdown ?? "";

            if (!vm.AutoUpdate)
            {
                vm.UpdateAvailable = true;
                vm.UpdateVersionText = $"Update available: v{targetVersion}";
                return;
            }

            if (vm.ShowReleaseNotesAfterUpdate)
            {
                var dialog = new ReleaseNotesDialog(targetVersion, releaseNotes, showUpdate: true);
                var shouldUpdate = await dialog.ShowDialog<bool>(owner);
                if (!shouldUpdate)
                    return;
            }

            await mgr.DownloadUpdatesAsync(updateInfo);
            mgr.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Auto-update check failed: {ex}");
        }
    }
}
