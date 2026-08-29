using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Models;
using RemotePC.Services;

namespace RemotePC.ViewModels;

public partial class PcViewModel : ObservableObject
{
    private static readonly TimeSpan BootTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RustDeskStartupDelay = TimeSpan.FromSeconds(10);

    private readonly SupabaseService _supabaseService;
    private readonly PcStatusService _statusService;
    private readonly RustDeskService _rustDeskService;
    private readonly CancellationToken _applicationCancellationToken;
    private CancellationTokenSource? _connectCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool isBusy;

    [ObservableProperty]
    private bool isOnline;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool isCurrentMachine;

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

    public string ButtonText => IsBusy ? "Cancel" : !Device.Enabled ? "Disabled" : IsCurrentMachine ? "This PC" : "Connect";

    public bool HasDisplayName => !string.IsNullOrWhiteSpace(Device.DisplayName);

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasTroubleshooting => !string.IsNullOrWhiteSpace(TroubleshootingText);

    public string TroubleshootingText => GetTroubleshootingText();

    partial void OnIsOnlineChanged(bool value)
    {
        OnPropertyChanged(nameof(ButtonText));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ButtonText));
    }

    partial void OnIsCurrentMachineChanged(bool value)
    {
        OnPropertyChanged(nameof(ButtonText));
    }

    partial void OnStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(TroubleshootingText));
        OnPropertyChanged(nameof(HasTroubleshooting));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(TroubleshootingText));
        OnPropertyChanged(nameof(HasTroubleshooting));
    }

    public async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        IsCurrentMachine = await IsCurrentMachineAsync(cancellationToken);
        if (!Device.Enabled)
        {
            IsOnline = false;
            StatusText = "Disabled";
            return;
        }

        if (IsCurrentMachine)
        {
            IsOnline = true;
            StatusText = "This PC";
            return;
        }

        if (string.IsNullOrWhiteSpace(Device.TailscaleIp))
        {
            IsOnline = false;
            StatusText = "Missing Tailscale IP";
            return;
        }

        StatusText = "Checking...";
        IsOnline = await IsPcOnlineAsync(cancellationToken);
        StatusText = IsOnline ? "Online" : "Offline";
    }

    [RelayCommand(CanExecute = nameof(CanConnect), AllowConcurrentExecutions = true)]
    private async Task ConnectAsync()
    {
        if (IsBusy)
        {
            StatusText = "Cancelling...";
            _connectCts?.Cancel();
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_applicationCancellationToken);
        _connectCts = linkedCts;
        var cancellationToken = linkedCts.Token;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            if (!Device.Enabled)
            {
                IsOnline = false;
                StatusText = "Disabled";
                ErrorMessage = "This PC is disabled in Supabase.";
                return;
            }

            IsCurrentMachine = await IsCurrentMachineAsync(cancellationToken);
            if (IsCurrentMachine)
            {
                IsOnline = true;
                StatusText = "This PC";
                ErrorMessage = "You are already on this PC.";
                return;
            }

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
            IsOnline = await IsPcOnlineAsync(cancellationToken);

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

            IsOnline = await WaitUntilOnlineAsync(
                BootTimeout,
                PollInterval,
                progress,
                cancellationToken);

            if (!IsOnline)
            {
                StatusText = "Opening RustDesk...";
                ErrorMessage = "Wake command was sent, but Tailscale did not confirm the PC is online. Opening RustDesk anyway.";
                await OpenRustDeskAsync(cancellationToken);
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
        catch (OperationCanceledException)
        {
            IsOnline = await SafeCheckOnlineAsync();
            StatusText = IsOnline ? "Online" : "Cancelled";
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
            if (ReferenceEquals(_connectCts, linkedCts))
            {
                _connectCts = null;
            }

            IsBusy = false;
        }
    }

    private bool CanConnect()
    {
        return IsBusy || (!IsCurrentMachine && Device.Enabled);
    }

    private async Task<bool> IsCurrentMachineAsync(CancellationToken cancellationToken)
    {
        var isLocalTailscaleIp = _statusService.IsLocalTailscaleIp(Device.TailscaleIp);
        if (isLocalTailscaleIp)
        {
            return true;
        }

        var isLocalRustDeskId = await _rustDeskService.IsLocalRustDeskIdAsync(Device.RustDeskId, cancellationToken);
        return isLocalRustDeskId == true;
    }

    private async Task<bool> IsPcOnlineAsync(CancellationToken cancellationToken)
    {
        var tailscaleTask = _statusService.IsReachableAsync(Device.TailscaleIp, cancellationToken);
        var rustDeskTask = _rustDeskService.IsPeerOnlineAsync(Device.RustDeskId, cancellationToken);

        await Task.WhenAll(tailscaleTask, rustDeskTask);
        return tailscaleTask.Result || rustDeskTask.Result == true;
    }

    private async Task<bool> WaitUntilOnlineAsync(
        TimeSpan timeout,
        TimeSpan pollInterval,
        IProgress<TimeSpan>? remainingProgress,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsPcOnlineAsync(cancellationToken))
            {
                return true;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            remainingProgress?.Report(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);

            var delay = remaining < pollInterval ? remaining : pollInterval;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        return false;
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
            return await IsPcOnlineAsync(_applicationCancellationToken);
        }
        catch
        {
            return false;
        }
    }

    private string GetTroubleshootingText()
    {
        if (StatusText.Equals("Missing Tailscale IP", StringComparison.OrdinalIgnoreCase))
        {
            return "Check: add the target PC's Tailscale IP to Supabase.";
        }

        if (StatusText.Equals("RustDesk ID not configured", StringComparison.OrdinalIgnoreCase))
        {
            return "Check: add rustdesk_id in Supabase, remove spaces, never add the password.";
        }

        if (StatusText.Equals("RustDesk is not installed", StringComparison.OrdinalIgnoreCase))
        {
            return "Check: install RustDesk on this laptop, then retry.";
        }

        if (StatusText.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return "Check: set enabled to true in Supabase to allow connections.";
        }

        if (StatusText.Equals("This PC", StringComparison.OrdinalIgnoreCase))
        {
            return "This row matches the computer you are using.";
        }

        if (StatusText.Equals("Failed to wake", StringComparison.OrdinalIgnoreCase))
        {
            return "Check: PC power, BIOS Wake-on-LAN, ESP32 polling, and Ethernet.";
        }

        if (StatusText.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return "The connect attempt was stopped. Press Connect to try again.";
        }

        if (ErrorMessage?.Contains("Wake command", StringComparison.OrdinalIgnoreCase) == true ||
            ErrorMessage?.Contains("Supabase", StringComparison.OrdinalIgnoreCase) == true ||
            ErrorMessage?.Contains("wake_pc", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Check: Supabase settings, wake_pc RPC, enabled row, and command_id.";
        }

        if (!string.IsNullOrWhiteSpace(ErrorMessage))
        {
            return "Check: RustDesk ID, RustDesk install, Tailscale IP, then refresh.";
        }

        return string.Empty;
    }
}
