using System.Text.Json.Serialization;
using RemotePC.Services;

namespace RemotePC.Models;

public sealed class PcDevice
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("device_name")]
    public string DeviceName { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("command_id")]
    public long CommandId { get; init; }

    [JsonPropertyName("tailscale_ip")]
    public string? TailscaleIp { get; init; }

    [JsonPropertyName("rustdesk_id")]
    public string? RustDeskId { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("last_seen")]
    public DateTimeOffset? LastSeen { get; init; }

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("remote_port")]
    public int RemotePort { get; init; } = LocalAppOptions.DefaultRemotePort;

    [JsonPropertyName("remote_enabled")]
    public bool RemoteEnabled { get; init; }

    [JsonPropertyName("remote_device_id")]
    public Guid? RemoteDeviceId { get; init; }

    [JsonPropertyName("remote_version")]
    public string? RemoteVersion { get; init; }

    [JsonPropertyName("mac_address")]
    public string? MacAddress { get; init; }

    [JsonPropertyName("wake_agent")]
    public string? WakeAgent { get; init; }

    [JsonPropertyName("wol_port")]
    public int WolPort { get; init; } = 9;

    public string FriendlyName => string.IsNullOrWhiteSpace(DisplayName) ? DeviceName : DisplayName;

    public string TailscaleHost => TailscaleIpAddress.Normalize(TailscaleIp);
}
