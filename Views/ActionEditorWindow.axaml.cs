using Avalonia.Controls;
using RemotePC.Models;
using RemotePC.ViewModels;

namespace RemotePC.Views;

public partial class ActionEditorWindow : Window
{
    public ActionEditorWindow()
    {
        InitializeComponent();
    }

    public ActionEditorWindow(PcDevice device, PcCommand? action, AdvancedWindowViewModel advanced)
        : this()
    {
        var viewModel = new ActionEditorWindowViewModel(device, action, advanced);
        viewModel.CloseRequested += OnCloseRequested;
        DataContext = viewModel;
    }

    private void OnCloseRequested(object? sender, bool saved)
    {
        if (DataContext is ActionEditorWindowViewModel viewModel)
        {
            viewModel.CloseRequested -= OnCloseRequested;
            Close(saved);
        }
    }

    private async void OnDeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ActionEditorWindowViewModel viewModel)
        {
            return;
        }

        var confirm = new ConfirmActionWindow("Delete Action", "Delete this saved action?", "Delete");
        if (await confirm.ShowDialog<bool>(this))
        {
            await viewModel.DeleteAsync();
        }
    }

    private async void OnTestClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ActionEditorWindowViewModel viewModel)
        {
            return;
        }

        var prompt = new PasswordPromptWindow("Test Action");
        var password = await prompt.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(password))
        {
            await viewModel.TestAsync(password);
        }
    }
}
