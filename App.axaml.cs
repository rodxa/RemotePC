using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RemotePC.Configuration;
using RemotePC.Services;
using RemotePC.ViewModels;
using RemotePC.Views;

namespace RemotePC;

public partial class App : Application
{
    private readonly CancellationTokenSource _shutdown = new();
    private HttpClient? _httpClient;
    private SupabaseService? _supabaseService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = AppConfiguration.Load();
            _httpClient = new HttpClient();
            _supabaseService = new SupabaseService(_httpClient, options);
            var statusService = new PcStatusService();
            var parsecService = new ParsecService();

            var viewModel = new MainWindowViewModel(
                _supabaseService,
                statusService,
                parsecService,
                _shutdown.Token);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            desktop.Exit += (_, _) =>
            {
                _shutdown.Cancel();
                _shutdown.Dispose();
                _supabaseService.Dispose();
                _httpClient.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
