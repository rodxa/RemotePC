using System.Text.Json.Serialization;

namespace RemotePC.Models;

public sealed class PcCommand
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("pc_id")]
    public long PcId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("command_type")]
    public string CommandType { get; init; } = PcCommandTypes.PowerShell;

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }

    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; init; }

    [JsonPropertyName("require_confirmation")]
    public bool RequireConfirmation { get; init; } = true;

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; init; } = 30;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    public string DisplayCategory => string.IsNullOrWhiteSpace(Category) ? "Custom" : Category;
}

public static class PcCommandTypes
{
    public const string Builtin = "builtin";
    public const string PowerShell = "powershell";
    public const string Process = "process";
}

public sealed class PcCommandSaveRequest
{
    public long? Id { get; init; }

    public long PcId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? Category { get; init; }

    public string CommandType { get; init; } = PcCommandTypes.PowerShell;

    public string? Command { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool RequireConfirmation { get; init; } = true;

    public int TimeoutSeconds { get; init; } = 30;

    public bool Enabled { get; init; } = true;

    public int SortOrder { get; init; }
}
