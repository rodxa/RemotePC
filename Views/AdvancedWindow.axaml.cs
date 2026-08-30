using Avalonia.Controls;
using RemotePC.Models;
using RemotePC.Services;
using RemotePC.ViewModels;

namespace RemotePC.Views;

public partial class AdvancedWindow : Window
{
    public AdvancedWindow()
    {
        InitializeComponent();
    }

    public AdvancedWindow(PcDevice device, SupabaseService supabase, RemoteHostClient remoteHostClient, CancellationToken cancellationToken)
        : this()
    {
        DataContext = new AdvancedWindowViewModel(device, supabase, remoteHostClient, cancellationToken);
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (DataContext is AdvancedWindowViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }

    private async void OnShutdownClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AdvancedWindowViewModel viewModel && await ConfirmAsync("Shutdown this PC?"))
        {
            await viewModel.ShutdownAsync(confirmed: true);
        }
    }

    private async void OnRestartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AdvancedWindowViewModel viewModel && await ConfirmAsync("Restart this PC?"))
        {
            await viewModel.RestartAsync(confirmed: true);
        }
    }

    private async void OnLockClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AdvancedWindowViewModel viewModel)
        {
            await viewModel.LockAsync();
        }
    }

    private async void OnRunActionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ActionItemViewModel item } ||
            DataContext is not AdvancedWindowViewModel viewModel)
        {
            return;
        }

        var confirmed = !item.Action.RequireConfirmation || await ConfirmAsync($"Run {item.Action.Name}?");
        if (confirmed)
        {
            await viewModel.ExecuteActionAsync(item.Action, confirmed: true);
        }
    }

    private async void OnAddActionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not AdvancedWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new ActionEditorWindow(viewModel.Device, null, viewModel);
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved)
        {
            await viewModel.LoadAsync();
        }
    }

    private async void OnEditActionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ActionItemViewModel item } ||
            DataContext is not AdvancedWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new ActionEditorWindow(viewModel.Device, item.Action, viewModel);
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved)
        {
            await viewModel.LoadAsync();
        }
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new ConfirmActionWindow("Confirm Action", message, "Run");
        return await dialog.ShowDialog<bool>(this);
    }
}
