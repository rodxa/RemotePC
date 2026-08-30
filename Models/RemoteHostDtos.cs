using System.Text.Json.Serialization;

namespace RemotePC.Models;

public sealed class RemoteHostHealth
{
    [JsonPropertyName("machineName")]
    public string MachineName { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("hostEnabled")]
    public bool HostEnabled { get; init; }

    [JsonPropertyName("hostDeviceId")]
    public string HostDeviceId { get; init; } = string.Empty;

    [JsonPropertyName("uptimeSeconds")]
    public long UptimeSeconds { get; init; }
}

public sealed class RemoteActionRequest
{
    [JsonPropertyName("confirmed")]
    public bool Confirmed { get; init; }
}

public sealed class ActionExecutionResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; init; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    [JsonPropertyName("stdout")]
    public string Stdout { get; init; } = string.Empty;

    [JsonPropertyName("stderr")]
    public string Stderr { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    public static ActionExecutionResult Failed(string message)
    {
        return new ActionExecutionResult
        {
            Success = false,
            Message = message
        };
    }
}

public sealed class RemotePasswordResponse
{
    [JsonPropertyName("hostDeviceId")]
    public string HostDeviceId { get; init; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;
}

public sealed class RemotePasswordRequest
{
    [JsonPropertyName("password")]
    public string Password { get; init; } = string.Empty;

    [JsonPropertyName("clientDeviceId")]
    public string ClientDeviceId { get; init; } = string.Empty;
}
