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

            mainWindow.Opened += (_, _) => _ = CheckForUpdatesOnStartup(mainWindow, vm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task CheckForUpdatesOnStartup(Window owner, MainWindowViewModel vm)
    {
        try
        {
            var settings = UserSettingsStore.Load();
            if (vm.ShowReleaseNotesAfterUpdate
                && !string.IsNullOrEmpty(settings.PendingReleaseNotesVersion))
            {
                var dialog = new ReleaseNotesDialog(
                    settings.PendingReleaseNotesVersion,
                    settings.PendingReleaseNotesContent ?? "",
                    showUpdate: false);
                await dialog.ShowDialog(owner);

                settings.PendingReleaseNotesVersion = null;
                settings.PendingReleaseNotesContent = null;
                vm.PendingReleaseNotesVersion = null;
                vm.PendingReleaseNotesContent = null;
                UserSettingsStore.Save(settings);
            }

            var mgr = UpdateService.CreateUpdateManager(vm.ReceiveExperimentalUpdates);
            var updateInfo = await mgr.CheckForUpdatesAsync();
            if (updateInfo is null)
                return;

            var targetVersion = updateInfo.TargetFullRelease.Version.ToString();
            var releaseNotes = updateInfo.TargetFullRelease.NotesMarkdown ?? "";

            void SavePendingReleaseNotes()
            {
                vm.PendingReleaseNotesVersion = targetVersion;
                vm.PendingReleaseNotesContent = releaseNotes;
                UserSettingsStore.Save(vm.CreateUserSettings());
            }

            vm.UpdateAvailable = true;
            vm.UpdateVersionText = $"v{targetVersion}";
            vm.UpdateReleaseNotes = releaseNotes;

            vm.ShowPendingReleaseNotes = async () =>
            {
                var dialog = new ReleaseNotesDialog(targetVersion, releaseNotes, showUpdate: false);
                await dialog.ShowDialog(owner);
            };

            vm.ApplyPendingUpdate = async () =>
            {
                SavePendingReleaseNotes();
                await mgr.DownloadUpdatesAsync(updateInfo);
                mgr.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
            };

            if (vm.AutoUpdate)
            {
                SavePendingReleaseNotes();
                await mgr.DownloadUpdatesAsync(updateInfo);
                mgr.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Auto-update check failed: {ex}");
        }
    }
}
