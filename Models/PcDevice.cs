using System.Text.Json.Serialization;

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

    public string FriendlyName => string.IsNullOrWhiteSpace(DisplayName) ? DeviceName : DisplayName;
}
