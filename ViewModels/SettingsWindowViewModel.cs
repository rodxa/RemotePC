using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Configuration;
using RemotePC.Models;
using RemotePC.Services;

namespace RemotePC.ViewModels;

public partial class SettingsWindowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string supabaseUrl = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string publishableKey = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool startWithWindows;

    [ObservableProperty]
    private bool startMinimized;

    [ObservableProperty]
    private bool closeToTray = true;

    [ObservableProperty]
    private bool remoteControlEnabled;

    [ObservableProperty]
    private bool notificationsEnabled = true;

    [ObservableProperty]
    private string machineName = Environment.MachineName;

    [ObservableProperty]
    private string remotePort = LocalAppOptions.DefaultRemotePort.ToString();

    [ObservableProperty]
    private string? pairingCode;

    [ObservableProperty]
    private string? pairingStatus;

    public SettingsWindowViewModel()
    {
        var settings = AppConfiguration.LoadAll();
        SupabaseUrl = settings.Supabase.IsConfigured ? settings.Supabase.Url : string.Empty;
        PublishableKey = settings.Supabase.IsConfigured ? settings.Supabase.PublishableKey : string.Empty;
        StartWithWindows = settings.Local.StartWithWindows;
        StartMinimized = settings.Local.StartMinimized;
        CloseToTray = settings.Local.CloseToTray;
        RemoteControlEnabled = settings.Local.RemoteControlEnabled;
        NotificationsEnabled = settings.Local.NotificationsEnabled;
        MachineName = settings.Local.MachineName;
        RemotePort = settings.Local.RemotePort.ToString();
    }

    public event EventHandler<bool>? CloseRequested;

    public string SettingsPath => AppConfiguration.GetSettingsPath();

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasPairingCode => !string.IsNullOrWhiteSpace(PairingCode);

    public string TailscaleStatus => new TailscaleService().GetLocalTailscaleIp() is { } ip
        ? ip
        : "Not detected";

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnPairingCodeChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPairingCode));
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var options = new SupabaseOptions
        {
            Url = SupabaseUrl,
            PublishableKey = PublishableKey
        }.Validated();

        if (!options.IsConfigured)
        {
            ErrorMessage = options.ConfigurationError;
            return;
        }

        if (!int.TryParse(RemotePort, out var remotePort) || remotePort is <= 0 or > 65535)
        {
            ErrorMessage = "Host port must be between 1 and 65535.";
            return;
        }

        try
        {
            AppConfiguration.SaveAll(new AppConfiguration.AppSettings
            {
                Supabase = options,
                Local = new LocalAppOptions
                {
                    StartWithWindows = StartWithWindows,
                    StartMinimized = StartMinimized,
                    CloseToTray = CloseToTray,
                    RemoteControlEnabled = RemoteControlEnabled,
                    NotificationsEnabled = NotificationsEnabled,
                    MachineName = MachineName,
                    RemotePort = remotePort
                }
            });
            ErrorMessage = null;
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Could not save settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CreatePairingCode()
    {
        var app = Application.Current as App;
        var code = app?.CreatePairingCode();
        if (code is null)
        {
            PairingCode = null;
            PairingStatus = "Enable host mode, save settings, then create a pairing code.";
            return;
        }

        PairingCode = code.Code;
        PairingStatus = $"Expires {code.ExpiresAtText}";
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, false);
    }

    private bool CanSave()
    {
        return !string.IsNullOrWhiteSpace(SupabaseUrl) &&
               !string.IsNullOrWhiteSpace(PublishableKey);
    }
}
