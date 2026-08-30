using Avalonia;

namespace RemotePC;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstance = new Services.SingleInstanceCoordinator();
        if (!singleInstance.IsPrimary)
        {
            Services.SingleInstanceCoordinator.SignalExistingAsync().GetAwaiter().GetResult();
            return;
        }

        App.SingleInstance = singleInstance;
        singleInstance.StartListening();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
