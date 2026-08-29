using Avalonia.Controls;

namespace RemotePC.Views;

public partial class ConfirmDeleteWindow : Window
{
    public ConfirmDeleteWindow()
    {
        InitializeComponent();
    }

    public ConfirmDeleteWindow(string pcName)
        : this()
    {
        MessageText.Text = $"This removes {pcName} from Supabase. Wake-on-LAN and RustDesk settings on the PC are not changed.";
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnDeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }
}
