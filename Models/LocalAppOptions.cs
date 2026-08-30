using System.Text.Json.Serialization;

namespace RemotePC.Models;

public sealed class LocalAppOptions
{
    public const int DefaultRemotePort = 47632;

    [JsonPropertyName("StartWithWindows")]
    public bool StartWithWindows { get; init; }

    [JsonPropertyName("StartMinimized")]
    public bool StartMinimized { get; init; }

    [JsonPropertyName("CloseToTray")]
    public bool CloseToTray { get; init; } = true;

    [JsonPropertyName("RemoteControlEnabled")]
    public bool RemoteControlEnabled { get; init; }

    [JsonPropertyName("NotificationsEnabled")]
    public bool NotificationsEnabled { get; init; } = true;

    [JsonPropertyName("MachineName")]
    public string MachineName { get; init; } = Environment.MachineName;

    [JsonPropertyName("RemotePort")]
    public int RemotePort { get; init; } = DefaultRemotePort;

    public LocalAppOptions Normalized()
    {
        var machineName = string.IsNullOrWhiteSpace(MachineName)
            ? Environment.MachineName
            : MachineName.Trim();

        return new LocalAppOptions
        {
            StartWithWindows = StartWithWindows,
            StartMinimized = StartMinimized,
            CloseToTray = CloseToTray,
            RemoteControlEnabled = RemoteControlEnabled,
            NotificationsEnabled = NotificationsEnabled,
            MachineName = machineName,
            RemotePort = RemotePort is > 0 and <= 65535 ? RemotePort : DefaultRemotePort
        };
    }
}
