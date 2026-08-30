namespace RemotePC.Models;

public sealed class PcDeviceCreateRequest
{
    public const int DefaultWolPort = 9;

    public string DeviceName { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? TailscaleIp { get; init; }

    public string RustDeskId { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public int RemotePort { get; init; } = LocalAppOptions.DefaultRemotePort;

    public bool RemoteEnabled { get; init; }

    public Guid? RemoteDeviceId { get; init; }

    public string? MacAddress { get; init; }

    public string WakeAgent { get; init; } = "home";

    public int WolPort { get; init; } = DefaultWolPort;
}
