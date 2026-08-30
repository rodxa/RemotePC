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
            var password = await PromptPasswordAsync("Shutdown");
            if (!string.IsNullOrWhiteSpace(password))
            {
                await viewModel.ShutdownAsync(true, password);
            }
        }
    }

    private async void OnRestartClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AdvancedWindowViewModel viewModel && await ConfirmAsync("Restart this PC?"))
        {
            var password = await PromptPasswordAsync("Restart");
            if (!string.IsNullOrWhiteSpace(password))
            {
                await viewModel.RestartAsync(true, password);
            }
        }
    }

    private async void OnLockClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AdvancedWindowViewModel viewModel)
        {
            var password = await PromptPasswordAsync("Lock");
            if (!string.IsNullOrWhiteSpace(password))
            {
                await viewModel.LockAsync(password);
            }
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
            var password = await PromptPasswordAsync(item.Action.Name);
            if (!string.IsNullOrWhiteSpace(password))
            {
                await viewModel.ExecuteActionAsync(item.Action, true, password);
            }
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

    private async Task<string?> PromptPasswordAsync(string actionName)
    {
        var dialog = new PasswordPromptWindow(actionName);
        return await dialog.ShowDialog<string?>(this);
    }
}
