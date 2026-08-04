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
            // --- Just-updated check ---
            var settings = UserSettingsStore.Load();
            if (!string.IsNullOrEmpty(settings.PendingReleaseNotesVersion))
            {
                if (vm.ShowReleaseNotesAfterUpdate)
                {
                    var notes = UpdateService.ProcessReleaseNotes(
                        settings.PendingReleaseNotesContent,
                        settings.PendingReleaseNotesVersion);
                    var dialog = new ReleaseNotesDialog(
                        settings.PendingReleaseNotesVersion, notes, showUpdate: false);
                    await dialog.ShowDialog(owner);
                }

                // Always clear pending notes
                settings.PendingReleaseNotesVersion = null;
                settings.PendingReleaseNotesContent = null;
                vm.PendingReleaseNotesVersion = null;
                vm.PendingReleaseNotesContent = null;
                UserSettingsStore.Save(settings);
            }

            // --- Update check ---
            var mgr = UpdateService.CreateUpdateManager(vm.ReceiveExperimentalUpdates);
            var updateInfo = await mgr.CheckForUpdatesAsync();
            if (updateInfo is null)
                return;

            var targetVersion = updateInfo.TargetFullRelease.Version.ToString();
            var rawNotes = updateInfo.TargetFullRelease.NotesMarkdown;
            var processedNotes = UpdateService.ProcessReleaseNotes(rawNotes, targetVersion);

            // Auto-update: download then save pending notes then restart
            if (vm.AutoUpdate)
            {
                await mgr.DownloadUpdatesAsync(updateInfo);
                vm.PendingReleaseNotesVersion = targetVersion;
                vm.PendingReleaseNotesContent = rawNotes;
                UserSettingsStore.Save(vm.CreateUserSettings());
                mgr.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
                return;
            }

            // Manual update path - first time seeing?
            bool firstTimeSeen = vm.LastSeenUpdateVersion != targetVersion;
            if (firstTimeSeen)
            {
                var dialog = new ReleaseNotesDialog(targetVersion, processedNotes, showUpdate: true);
                var shouldUpdate = await dialog.ShowDialog<bool>(owner);

                if (shouldUpdate)
                {
                    await mgr.DownloadUpdatesAsync(updateInfo);
                    vm.PendingReleaseNotesVersion = targetVersion;
                    vm.PendingReleaseNotesContent = rawNotes;
                    UserSettingsStore.Save(vm.CreateUserSettings());
                    mgr.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
                    return;
                }

                // Declined - remember we've seen this version
                vm.LastSeenUpdateVersion = targetVersion;
                UserSettingsStore.Save(vm.CreateUserSettings());
            }

            // Show update icon + wire delegates
            vm.UpdateAvailable = true;
            vm.UpdateVersionText = $"v{targetVersion}";

            vm.ShowPendingReleaseNotes = async () =>
            {
                var dialog = new ReleaseNotesDialog(targetVersion, processedNotes, showUpdate: true);
                var shouldUpdate = await dialog.ShowDialog<bool>(owner);
                if (shouldUpdate && vm.ApplyPendingUpdate is not null)
                    await vm.ApplyPendingUpdate();
            };

            vm.ApplyPendingUpdate = async () =>
            {
                await mgr.DownloadUpdatesAsync(updateInfo);
                vm.PendingReleaseNotesVersion = targetVersion;
                vm.PendingReleaseNotesContent = rawNotes;
                UserSettingsStore.Save(vm.CreateUserSettings());
                mgr.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Auto-update check failed: {ex}");
        }
    }
}
