using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Models;
using RemotePC.Services;

namespace RemotePC.ViewModels;

public partial class PcViewModel : ObservableObject
{
    private static readonly TimeSpan BootTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly SupabaseService _supabaseService;
    private readonly PcStatusService _statusService;
    private readonly ParsecService _parsecService;
    private readonly CancellationToken _applicationCancellationToken;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool isBusy;

    [ObservableProperty]
    private bool isOnline;

    [ObservableProperty]
    private string statusText = "Checking...";

    [ObservableProperty]
    private string? errorMessage;

    public PcViewModel(
        PcDevice device,
        SupabaseService supabaseService,
        PcStatusService statusService,
        ParsecService parsecService,
        CancellationToken applicationCancellationToken)
    {
        Device = device;
        _supabaseService = supabaseService;
        _statusService = statusService;
        _parsecService = parsecService;
        _applicationCancellationToken = applicationCancellationToken;
    }

    public PcDevice Device { get; }

    public string Name => Device.FriendlyName;

    public string DeviceName => Device.DeviceName;

    public string TailscaleIp => string.IsNullOrWhiteSpace(Device.TailscaleIp) ? "No Tailscale IP configured" : Device.TailscaleIp;

    public string LastSeenText => Device.LastSeen is null ? "No heartbeat yet" : $"Last seen {Device.LastSeen.Value.LocalDateTime:g}";

    public string ButtonText => IsOnline ? "Connect" : "Wake";

    public bool HasDisplayName => !string.IsNullOrWhiteSpace(Device.DisplayName);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnIsOnlineChanged(bool value)
    {
        OnPropertyChanged(nameof(ButtonText));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    public async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Device.TailscaleIp))
        {
            IsOnline = false;
            StatusText = "Missing Tailscale IP";
            return;
        }

        StatusText = "Checking...";
        IsOnline = await _statusService.IsReachableAsync(Device.TailscaleIp, cancellationToken);
        StatusText = IsOnline ? "Online" : "Offline";
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (IsBusy)
        {
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_applicationCancellationToken);
        var cancellationToken = linkedCts.Token;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            if (string.IsNullOrWhiteSpace(Device.TailscaleIp))
            {
                IsOnline = false;
                StatusText = "Missing Tailscale IP";
                ErrorMessage = "Add a Tailscale IP for this PC in Supabase.";
                return;
            }

            StatusText = "Checking...";
            IsOnline = await _statusService.IsReachableAsync(Device.TailscaleIp, cancellationToken);

            if (IsOnline)
            {
                await OpenParsecAsync(cancellationToken);
                return;
            }

            StatusText = "Sending wake command...";
            await _supabaseService.WakePcAsync(Device.Id, cancellationToken);

            StatusText = "Waiting for PC...";
            var progress = new Progress<TimeSpan>(remaining =>
            {
                StatusText = $"Waiting for PC... {Math.Ceiling(remaining.TotalSeconds):0}s";
            });

            IsOnline = await _statusService.WaitUntilReachableAsync(
                Device.TailscaleIp,
                BootTimeout,
                PollInterval,
                progress,
                cancellationToken);

            if (!IsOnline)
            {
                StatusText = "Wake timed out";
                ErrorMessage = "The PC did not become reachable through Tailscale within 90 seconds.";
                return;
            }

            StatusText = "Online";
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            await OpenParsecAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (_applicationCancellationToken.IsCancellationRequested)
        {
            StatusText = "Cancelled";
        }
        catch (Exception ex) when (ex is SupabaseException or FileNotFoundException or IOException or InvalidOperationException)
        {
            IsOnline = await SafeCheckOnlineAsync();
            StatusText = IsOnline ? "Online" : "Offline";
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect()
    {
        return !IsBusy;
    }

    private async Task OpenParsecAsync(CancellationToken cancellationToken)
    {
        StatusText = "Opening Parsec...";
        await _parsecService.LaunchAsync(Device.ParsecPeerId, cancellationToken);
        StatusText = "Parsec opened";
    }

    private async Task<bool> SafeCheckOnlineAsync()
    {
        try
        {
            return await _statusService.IsReachableAsync(Device.TailscaleIp, _applicationCancellationToken);
        }
        catch
        {
            return false;
        }
    }
}
