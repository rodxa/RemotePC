using Avalonia.Controls;
using RemotePC.ViewModels;

namespace RemotePC.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;

        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private async void OnOpenSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new SettingsWindow();
        var saved = await dialog.ShowDialog<bool>(this);

        if (saved && DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ReloadConfigurationAndRefreshAsync();
        }
    }
}
