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

    private async void OnAddDeviceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new AddDeviceWindow();
        var request = await dialog.ShowDialog<RemotePC.Models.PcDeviceCreateRequest?>(this);

        if (request is not null && DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.AddDeviceAsync(request);
        }
    }

    private async void OnEditDeviceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PcViewModel pc } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new AddDeviceWindow(pc.Device);
        var request = await dialog.ShowDialog<RemotePC.Models.PcDeviceCreateRequest?>(this);

        if (request is not null)
        {
            await viewModel.UpdateDeviceAsync(pc, request);
        }
    }

    private async void OnDeleteDeviceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PcViewModel pc } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new ConfirmDeleteWindow(pc.Name);
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (confirmed)
        {
            await viewModel.DeleteDeviceAsync(pc);
        }
    }
}
