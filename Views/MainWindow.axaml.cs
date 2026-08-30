using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using RemotePC.ViewModels;

namespace RemotePC.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        PropertyChanged += OnWindowPropertyChanged;
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
            if (Avalonia.Application.Current is App app)
            {
                await app.ReloadRuntimeSettingsAsync();
            }

            await viewModel.ReloadConfigurationAndRefreshAsync();
        }
    }

    private void OnWindowPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty &&
            WindowState == WindowState.Minimized &&
            Avalonia.Application.Current is App &&
            RemotePC.Configuration.AppConfiguration.LoadAll().Local.CloseToTray)
        {
            Dispatcher.UIThread.Post(Hide);
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

    private async void OnAdvancedDeviceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PcViewModel pc } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new AdvancedWindow(
            pc.Device,
            viewModel.SupabaseService,
            viewModel.RemoteHostClient,
            viewModel.ApplicationCancellationToken);
        await dialog.ShowDialog(this);
        await pc.RefreshStatusAsync(viewModel.ApplicationCancellationToken);
    }
}
