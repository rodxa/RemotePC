using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using RemotePC.Configuration;
using RemotePC.Models;
using RemotePC.Services;
using RemotePC.ViewModels;
using RemotePC.Views;

namespace RemotePC;

public partial class App : Application
{
    private readonly CancellationTokenSource _shutdown = new();
    private HttpClient? _httpClient;
    private HttpClient? _remoteHttpClient;
    private SupabaseService? _supabaseService;
    private PcStatusService? _statusService;
    private RustDeskService? _rustDeskService;
    private ProtectedCredentialStore? _credentialStore;
    private RemoteHostClient? _remoteHostClient;
    private RemoteHostServer? _remoteHostServer;
    private WindowsStartupService? _startupService;
    private TailscaleService? _tailscaleService;
    private PairingService? _pairingService;
    private AppLogger? _logger;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _hostToggleMenuItem;
    private NativeMenuItem? _statusMenuItem;
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainViewModel;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _isExplicitExit;

    public static SingleInstanceCoordinator? SingleInstance { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var settings = AppConfiguration.LoadAll();
            _httpClient = new HttpClient();
            _remoteHttpClient = new HttpClient();
            _supabaseService = new SupabaseService(_httpClient, settings.Supabase);
            _statusService = new PcStatusService();
            _rustDeskService = new RustDeskService();
            _credentialStore = new ProtectedCredentialStore();
            _remoteHostClient = new RemoteHostClient(_remoteHttpClient, _credentialStore);
            _startupService = new WindowsStartupService();
            _tailscaleService = new TailscaleService();
            _logger = new AppLogger();
            _pairingService = new PairingService();
            _remoteHostServer = new RemoteHostServer(
                _supabaseService,
                _credentialStore,
                _pairingService,
                new ActionExecutor(),
                _tailscaleService,
                _logger);

            _mainViewModel = new MainWindowViewModel(
                _supabaseService,
                _statusService,
                _rustDeskService,
                _remoteHostClient,
                _shutdown.Token);

            InitializeTray();
            SingleInstance!.OpenRequested += OnSingleInstanceOpenRequested;

            _ = _remoteHostServer.ApplySettingsAsync(settings.Local, _shutdown.Token);
            _logger.Info("RemotePC started");

            _mainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };
            _mainWindow.Closing += OnMainWindowClosing;
            if (!ShouldStartHidden(desktop.Args, settings.Local))
            {
                desktop.MainWindow = _mainWindow;
                ShowMainWindow();
            }

            desktop.Exit += (_, _) =>
            {
                _shutdown.Cancel();
                _shutdown.Dispose();
                _trayIcon?.Dispose();
                _supabaseService.Dispose();
                _httpClient.Dispose();
                _remoteHttpClient.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public async Task ReloadRuntimeSettingsAsync()
    {
        var settings = AppConfiguration.LoadAll();
        _supabaseService?.ReloadConfiguration();
        _startupService?.SetEnabled(settings.Local.StartWithWindows);
        if (_remoteHostServer is not null)
        {
            await _remoteHostServer.ApplySettingsAsync(settings.Local, _shutdown.Token);
        }

        UpdateTrayStatus();
    }

    public PairingCodeInfo? CreatePairingCode()
    {
        if (_remoteHostServer is not { IsRunning: true })
        {
            return null;
        }

        return _pairingService?.CreatePairingCode(TimeSpan.FromMinutes(5));
    }

    private void InitializeTray()
    {
        using var iconStream = CreateTrayIconStream();
        _trayIcon = new TrayIcon
        {
            ToolTipText = "RemotePC",
            Icon = new WindowIcon(iconStream),
            IsVisible = true
        };

        var menu = new NativeMenu();
        var openItem = new NativeMenuItem("Open RemotePC");
        openItem.Click += (_, _) => ShowMainWindow();
        _hostToggleMenuItem = new NativeMenuItem();
        _hostToggleMenuItem.Click += async (_, _) => await ToggleHostAsync();
        _statusMenuItem = new NativeMenuItem { IsEnabled = false };
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += async (_, _) => await ExitAsync();

        menu.Add(openItem);
        menu.Add(_hostToggleMenuItem);
        menu.Add(_statusMenuItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);
        _trayIcon.Menu = menu;
        _trayIcon.Clicked += (_, _) => ShowMainWindow();
        UpdateTrayStatus();
        _logger?.Info("Tray initialized");
    }

    private static MemoryStream CreateTrayIconStream()
    {
        const int width = 16;
        const int height = 16;
        const int bitmapHeaderSize = 40;
        const int pixelBytes = width * height * 4;
        const int maskBytes = 4 * height;
        const int imageBytes = bitmapHeaderSize + pixelBytes + maskBytes;
        const int imageOffset = 22;

        var stream = new MemoryStream();
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 1);

        stream.WriteByte(width);
        stream.WriteByte(height);
        stream.WriteByte(0);
        stream.WriteByte(0);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 32);
        WriteUInt32(stream, imageBytes);
        WriteUInt32(stream, imageOffset);

        WriteUInt32(stream, bitmapHeaderSize);
        WriteInt32(stream, width);
        WriteInt32(stream, height * 2);
        WriteUInt16(stream, 1);
        WriteUInt16(stream, 32);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, pixelBytes);
        WriteInt32(stream, 0);
        WriteInt32(stream, 0);
        WriteUInt32(stream, 0);
        WriteUInt32(stream, 0);

        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                var edge = x is 0 or width - 1 || y is 0 or height - 1;
                stream.WriteByte(edge ? (byte)0x4C : (byte)0x91);
                stream.WriteByte(edge ? (byte)0x7A : (byte)0xC7);
                stream.WriteByte(edge ? (byte)0x1E : (byte)0x2F);
                stream.WriteByte(0xFF);
            }
        }

        stream.Write(new byte[maskBytes]);
        stream.Position = 0;
        return stream;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.Write(BitConverter.GetBytes(value));
    }

    private static void WriteUInt32(Stream stream, int value)
    {
        stream.Write(BitConverter.GetBytes((uint)value));
    }

    private static void WriteInt32(Stream stream, int value)
    {
        stream.Write(BitConverter.GetBytes(value));
    }

    private async Task ToggleHostAsync()
    {
        var settings = AppConfiguration.LoadAll();
        var local = settings.Local.Normalized();
        AppConfiguration.SaveAll(new AppConfiguration.AppSettings
        {
            Supabase = settings.Supabase,
            Local = new LocalAppOptions
            {
                StartWithWindows = local.StartWithWindows,
                StartMinimized = local.StartMinimized,
                CloseToTray = local.CloseToTray,
                RemoteControlEnabled = !local.RemoteControlEnabled,
                NotificationsEnabled = local.NotificationsEnabled,
                MachineName = local.MachineName,
                RemotePort = local.RemotePort
            }
        });
        await ReloadRuntimeSettingsAsync();
    }

    private static bool ShouldStartHidden(string[]? args, LocalAppOptions options)
    {
        return options.StartMinimized ||
               args?.Any(arg =>
                   arg.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (_mainWindow is null)
            {
                _mainWindow = new MainWindow
                {
                    DataContext = _mainViewModel
                };
                _mainWindow.Closing += OnMainWindowClosing;
                if (_desktop is not null)
                {
                    _desktop.MainWindow = _mainWindow;
                }
            }

            if (!_mainWindow.IsVisible)
            {
                _mainWindow.Show();
            }

            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Activate();

            if (_mainViewModel is not null && _mainViewModel.Pcs.Count == 0 && !_mainViewModel.IsLoading)
            {
                await _mainViewModel.InitializeAsync();
            }
        });
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExplicitExit)
        {
            return;
        }

        var settings = AppConfiguration.LoadAll();
        if (settings.Local.CloseToTray)
        {
            e.Cancel = true;
            _mainWindow?.Hide();
        }
    }

    private void OnSingleInstanceOpenRequested(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private async Task ExitAsync()
    {
        _isExplicitExit = true;
        _shutdown.Cancel();
        if (_remoteHostServer is not null)
        {
            await _remoteHostServer.StopAsync(CancellationToken.None);
        }

        _desktop?.Shutdown();
    }

    private void UpdateTrayStatus()
    {
        var local = AppConfiguration.LoadAll().Local;
        if (_hostToggleMenuItem is not null)
        {
            _hostToggleMenuItem.Header = local.RemoteControlEnabled ? "Host: Enabled" : "Host: Disabled";
        }

        if (_statusMenuItem is not null)
        {
            _statusMenuItem.Header = _remoteHostServer?.IsRunning == true
                ? $"Status: Listening on {local.RemotePort}"
                : "Status: Host stopped";
        }
    }
}
