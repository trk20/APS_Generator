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
    }

    public SettingsDialog(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        RegisterDialogHandlers();
        this.viewModel = viewModel;
        DataContext = viewModel;
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
