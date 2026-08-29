using Avalonia.Controls;
using RemotePC.ViewModels;

namespace RemotePC.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        var viewModel = new SettingsWindowViewModel();
        viewModel.CloseRequested += OnCloseRequested;
        DataContext = viewModel;
    }

    private void OnCloseRequested(object? sender, bool saved)
    {
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        Close(saved);
    }
}
