namespace RemotePC.Models;

public sealed class PcDeviceCreateRequest
{
    public string DeviceName { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? TailscaleIp { get; init; }

    public string RustDeskId { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;
}
