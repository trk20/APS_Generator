using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ApsGenerator.UI;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog() => InitializeComponent();

    public ConfirmationDialog(string message, double uiScale = 1.0)
    {
        DataContext = message;
        InitializeComponent();
        RootTransform.LayoutTransform = new ScaleTransform(uiScale, uiScale);
    }

    private void OnContinue(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
