using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Models;
using RemotePC.Services;

namespace RemotePC.ViewModels;

public partial class PcViewModel : ObservableObject
{
    private static readonly TimeSpan BootTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RustDeskStartupDelay = TimeSpan.FromSeconds(12);

    private readonly SupabaseService _supabaseService;
    private readonly PcStatusService _statusService;
    private readonly RustDeskService _rustDeskService;
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
        RustDeskService rustDeskService,
        CancellationToken applicationCancellationToken)
    {
        Device = device;
        _supabaseService = supabaseService;
        _statusService = statusService;
        _rustDeskService = rustDeskService;
        _applicationCancellationToken = applicationCancellationToken;
    }

    public PcDevice Device { get; }

    public string Name => Device.FriendlyName;

    public string DeviceName => Device.DeviceName;

    public string TailscaleIp => string.IsNullOrWhiteSpace(Device.TailscaleIp) ? "No Tailscale IP configured" : Device.TailscaleIp;

    public string LastSeenText => Device.LastSeen is null ? "No heartbeat yet" : $"Last seen {Device.LastSeen.Value.LocalDateTime:g}";

    public string ButtonText => "Connect";

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

            if (string.IsNullOrWhiteSpace(RustDeskService.NormalizeRustDeskId(Device.RustDeskId)))
            {
                StatusText = "RustDesk ID not configured";
                ErrorMessage = "Add this PC's RustDesk ID in Supabase.";
                return;
            }

            StatusText = "Checking...";
            IsOnline = await _statusService.IsReachableAsync(Device.TailscaleIp, cancellationToken);

            if (IsOnline)
            {
                await OpenRustDeskAsync(cancellationToken);
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
                StatusText = "Failed to wake";
                ErrorMessage = "The PC did not become reachable through Tailscale within 90 seconds.";
                return;
            }

            StatusText = "Waiting for RustDesk...";
            await Task.Delay(RustDeskStartupDelay, cancellationToken);
            await OpenRustDeskAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (_applicationCancellationToken.IsCancellationRequested)
        {
            StatusText = "Cancelled";
        }
        catch (FileNotFoundException ex)
        {
            IsOnline = await SafeCheckOnlineAsync();
            StatusText = "RustDesk is not installed";
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is SupabaseException or IOException or InvalidOperationException)
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

    private async Task OpenRustDeskAsync(CancellationToken cancellationToken)
    {
        StatusText = "Opening RustDesk...";
        await _rustDeskService.LaunchAsync(Device.RustDeskId, cancellationToken);
        StatusText = "Online";
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
