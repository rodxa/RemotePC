using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Models;
using RemotePC.Services;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RemotePC.ViewModels;

public partial class AddDeviceWindowViewModel : ObservableObject
{
    private static readonly Regex MacAddressRegex = new(
        "^([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly RemoteHostClient? _remoteHostClient;

    public AddDeviceWindowViewModel()
    {
    }

    public AddDeviceWindowViewModel(RemoteHostClient remoteHostClient)
    {
        _remoteHostClient = remoteHostClient;
    }

    public AddDeviceWindowViewModel(PcDevice device, RemoteHostClient? remoteHostClient = null)
    {
        _remoteHostClient = remoteHostClient;
        IsEditing = true;
        DeviceName = device.DeviceName;
        DisplayName = device.DisplayName ?? string.Empty;
        TailscaleIp = device.TailscaleIp ?? string.Empty;
        RustDeskId = device.RustDeskId ?? string.Empty;
        Enabled = device.Enabled;
        RemotePort = device.RemotePort.ToString(CultureInfo.InvariantCulture);
        RemoteEnabled = device.RemoteEnabled;
        RemoteDeviceId = device.RemoteDeviceId?.ToString("D") ?? string.Empty;
        MacAddress = device.MacAddress ?? string.Empty;
        WakeAgent = string.IsNullOrWhiteSpace(device.WakeAgent) ? "home" : device.WakeAgent;
        WolPort = (device.WolPort is > 0 and <= 65535 ? device.WolPort : PcDeviceCreateRequest.DefaultWolPort)
            .ToString(CultureInfo.InvariantCulture);
    }

    public bool IsEditing { get; }

    public string WindowTitle => IsEditing ? "Edit PC" : "Add PC";

    public string HeaderText => IsEditing ? "Edit PC" : "Add PC";

    public string SaveButtonText => IsEditing ? "Save" : "Add";

    [ObservableProperty]
    private string deviceName = string.Empty;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string tailscaleIp = string.Empty;

    [ObservableProperty]
    private string rustDeskId = string.Empty;

    [ObservableProperty]
    private bool enabled = true;

    [ObservableProperty]
    private string remotePort = LocalAppOptions.DefaultRemotePort.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private bool remoteEnabled;

    [ObservableProperty]
    private string remoteDeviceId = string.Empty;

    [ObservableProperty]
    private string macAddress = string.Empty;

    [ObservableProperty]
    private string wakeAgent = "home";

    [ObservableProperty]
    private string wolPort = PcDeviceCreateRequest.DefaultWolPort.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchRemoteDeviceIdCommand))]
    private bool isFetchingRemoteDeviceId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchMacAddressCommand))]
    private bool isFetchingMacAddress;

    [ObservableProperty]
    private string? errorMessage;

    public event EventHandler<PcDeviceCreateRequest>? SaveRequested;

    public event EventHandler? CancelRequested;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;

        var normalizedDeviceName = DeviceName.Trim();
        var normalizedRustDeskId = RustDeskService.NormalizeRustDeskId(RustDeskId);
        if (string.IsNullOrWhiteSpace(normalizedDeviceName))
        {
            ErrorMessage = "Device name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TailscaleIp))
        {
            ErrorMessage = "Tailscale IP is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedRustDeskId))
        {
            ErrorMessage = "RustDesk ID is required.";
            return;
        }

        if (!int.TryParse(RemotePort, NumberStyles.None, CultureInfo.InvariantCulture, out var normalizedRemotePort) ||
            normalizedRemotePort is <= 0 or > 65535)
        {
            ErrorMessage = "Remote port must be between 1 and 65535.";
            return;
        }

        var normalizedMacAddress = NormalizeMacAddress(MacAddress);
        if (normalizedMacAddress is null && !string.IsNullOrWhiteSpace(MacAddress))
        {
            ErrorMessage = "MAC address must look like 9C:6B:00:7B:DC:44.";
            return;
        }

        var normalizedWakeAgent = WakeAgent.Trim();
        if (string.IsNullOrWhiteSpace(normalizedWakeAgent))
        {
            ErrorMessage = "Wake agent is required.";
            return;
        }

        if (!int.TryParse(WolPort, NumberStyles.None, CultureInfo.InvariantCulture, out var normalizedWolPort) ||
            normalizedWolPort is <= 0 or > 65535)
        {
            ErrorMessage = "Wake-on-LAN port must be between 1 and 65535.";
            return;
        }

        Guid? normalizedRemoteDeviceId = null;
        if (!string.IsNullOrWhiteSpace(RemoteDeviceId))
        {
            if (!Guid.TryParse(RemoteDeviceId.Trim(), out var parsedRemoteDeviceId))
            {
                ErrorMessage = "Remote device ID must be a valid UUID.";
                return;
            }

            normalizedRemoteDeviceId = parsedRemoteDeviceId;
        }

        SaveRequested?.Invoke(
            this,
            new PcDeviceCreateRequest
            {
                DeviceName = normalizedDeviceName,
                DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
                TailscaleIp = TailscaleIp.Trim(),
                RustDeskId = normalizedRustDeskId,
                Enabled = Enabled,
                RemotePort = normalizedRemotePort,
                RemoteEnabled = RemoteEnabled,
                RemoteDeviceId = normalizedRemoteDeviceId,
                MacAddress = normalizedMacAddress,
                WakeAgent = normalizedWakeAgent,
                WolPort = normalizedWolPort
            });
    }

    [RelayCommand(CanExecute = nameof(CanFetchRemoteDeviceId))]
    private async Task FetchRemoteDeviceIdAsync()
    {
        ErrorMessage = null;

        if (_remoteHostClient is null)
        {
            ErrorMessage = "Remote host lookup is not available.";
            return;
        }

        var normalizedTailscaleIp = TailscaleIpAddress.Normalize(TailscaleIp);
        if (string.IsNullOrWhiteSpace(normalizedTailscaleIp))
        {
            ErrorMessage = "Tailscale IP is required before fetching the Remote Device ID.";
            return;
        }

        if (!int.TryParse(RemotePort, NumberStyles.None, CultureInfo.InvariantCulture, out var normalizedRemotePort) ||
            normalizedRemotePort is <= 0 or > 65535)
        {
            ErrorMessage = "Remote port must be between 1 and 65535.";
            return;
        }

        IsFetchingRemoteDeviceId = true;
        try
        {
            var health = await _remoteHostClient.GetHealthAsync(
                normalizedTailscaleIp,
                normalizedRemotePort,
                CancellationToken.None);

            if (health is null)
            {
                ErrorMessage = "RemotePC host was not reachable at that Tailscale IP and port.";
                return;
            }

            if (!Guid.TryParse(health.HostDeviceId, out var hostDeviceId))
            {
                ErrorMessage = "RemotePC host did not return a valid Remote Device ID.";
                return;
            }

            RemoteDeviceId = hostDeviceId.ToString("D");
            RemoteEnabled = health.HostEnabled;
            RemotePort = normalizedRemotePort.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            IsFetchingRemoteDeviceId = false;
        }
    }

    private bool CanFetchRemoteDeviceId()
    {
        return !IsFetchingRemoteDeviceId;
    }

    [RelayCommand(CanExecute = nameof(CanFetchMacAddress))]
    private async Task FetchMacAddressAsync()
    {
        ErrorMessage = null;

        if (_remoteHostClient is null)
        {
            ErrorMessage = "Remote host lookup is not available.";
            return;
        }

        var normalizedTailscaleIp = TailscaleIpAddress.Normalize(TailscaleIp);
        if (string.IsNullOrWhiteSpace(normalizedTailscaleIp))
        {
            ErrorMessage = "Tailscale IP is required before fetching the MAC address.";
            return;
        }

        if (!int.TryParse(RemotePort, NumberStyles.None, CultureInfo.InvariantCulture, out var normalizedRemotePort) ||
            normalizedRemotePort is <= 0 or > 65535)
        {
            ErrorMessage = "Remote port must be between 1 and 65535.";
            return;
        }

        IsFetchingMacAddress = true;
        try
        {
            var health = await _remoteHostClient.GetHealthAsync(
                normalizedTailscaleIp,
                normalizedRemotePort,
                CancellationToken.None);

            if (health is null)
            {
                ErrorMessage = "RemotePC host was not reachable at that Tailscale IP and port.";
                return;
            }

            var normalizedMacAddress = NormalizeMacAddress(health.MacAddress ?? string.Empty);
            if (normalizedMacAddress is null)
            {
                ErrorMessage = "RemotePC host did not return a usable physical MAC address.";
                return;
            }

            MacAddress = normalizedMacAddress;
        }
        finally
        {
            IsFetchingMacAddress = false;
        }
    }

    private bool CanFetchMacAddress()
    {
        return !IsFetchingMacAddress;
    }

    private static string? NormalizeMacAddress(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return MacAddressRegex.IsMatch(trimmed)
            ? trimmed.Replace('-', ':').ToUpperInvariant()
            : null;
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
