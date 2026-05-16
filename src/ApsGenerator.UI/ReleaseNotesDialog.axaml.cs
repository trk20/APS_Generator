using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ApsGenerator.UI;

public partial class ReleaseNotesDialog : Window
{
    public ReleaseNotesDialog()
    {
        InitializeComponent();
        UpdateButton.IsVisible = false;
        RegisterKeyHandler();
    }

    public ReleaseNotesDialog(string version, string releaseNotes, bool showUpdate)
    {
        InitializeComponent();
        VersionHeader.Text = $"Version {version}";
        ReleaseNotesContent.Markdown = string.IsNullOrWhiteSpace(releaseNotes)
            ? "No release notes available."
            : releaseNotes;
        UpdateButton.IsVisible = showUpdate;
        RegisterKeyHandler();
    }

    private void RegisterKeyHandler() =>
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close(false);
    }

    private void OnUpdateClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(false);
}
