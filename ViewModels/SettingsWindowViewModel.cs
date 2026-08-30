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

    private readonly ProtectedCredentialStore _credentials = new();

    [ObservableProperty]
    private string remoteControlPassword = string.Empty;

    [ObservableProperty]
    private string confirmRemoteControlPassword = string.Empty;

    [ObservableProperty]
    private string passwordStatus = string.Empty;

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
        PasswordStatus = _credentials.HasHostPassword()
            ? "Remote Command password is configured. Leave the fields empty to keep it."
            : "No Remote Command password is configured.";
    }

    public event EventHandler<bool>? CloseRequested;

    public string SettingsPath => AppConfiguration.GetSettingsPath();

    public string LocalDeviceId => _credentials.GetOrCreateLocalDeviceId();

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string TailscaleStatus => new TailscaleService().GetLocalTailscaleIp() is { } ip
        ? ip
        : "Not detected";

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
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

        if (!string.IsNullOrWhiteSpace(RemoteControlPassword) ||
            !string.IsNullOrWhiteSpace(ConfirmRemoteControlPassword))
        {
            if (RemoteControlPassword.Length < 8)
            {
                ErrorMessage = "Remote Command password must be at least 8 characters.";
                return;
            }

            if (!string.Equals(RemoteControlPassword, ConfirmRemoteControlPassword, StringComparison.Ordinal))
            {
                ErrorMessage = "Remote Command passwords do not match.";
                return;
            }
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
            if (!string.IsNullOrWhiteSpace(RemoteControlPassword))
            {
                _credentials.SetHostPassword(RemoteControlPassword);
            }

            ErrorMessage = null;
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Could not save settings: {ex.Message}";
        }
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
