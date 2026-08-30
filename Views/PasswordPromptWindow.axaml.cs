using Avalonia.Controls;

namespace RemotePC.Views;

public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow()
    {
        InitializeComponent();
    }

    public PasswordPromptWindow(string actionName)
        : this()
    {
        MessageText.Text = $"Enter the Remote Command password to run {actionName}.";
        Opened += (_, _) => PasswordBox.Focus();
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnRunClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(PasswordBox.Text);
    }
}
