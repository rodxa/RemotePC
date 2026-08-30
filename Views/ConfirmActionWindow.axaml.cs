using Avalonia.Controls;

namespace RemotePC.Views;

public partial class ConfirmActionWindow : Window
{
    public ConfirmActionWindow()
    {
        InitializeComponent();
    }

    public ConfirmActionWindow(string title, string message, string confirmText)
        : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnConfirmClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }
}
