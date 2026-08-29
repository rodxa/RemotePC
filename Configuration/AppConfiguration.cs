using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemotePC.Configuration;

public static class AppConfiguration
{
    private const string FileName = "appsettings.json";

    public static SupabaseOptions Load()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            return SupabaseOptions.Missing($"Create {FileName} next to the executable and add your Supabase URL and publishable key.");
        }

        try
        {
            var json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            var options = root?.Supabase ?? SupabaseOptions.Missing($"The {FileName} file does not contain a Supabase section.");
            return options.Validated();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return SupabaseOptions.Missing($"Could not read {FileName}: {ex.Message}");
        }
    }

    public static SupabaseOptions LoadForEditing()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            return new SupabaseOptions();
        }

        try
        {
            var json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return root?.Supabase ?? new SupabaseOptions();
        }
        catch
        {
            return new SupabaseOptions();
        }
    }

    public static void Save(SupabaseOptions options)
    {
        var path = GetSettingsPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var settings = new AppSettings
        {
            Supabase = new SupabaseOptions
            {
                Url = options.Url.Trim(),
                PublishableKey = options.PublishableKey.Trim()
            }
        };

        var json = JsonSerializer.Serialize(settings, WriteOptions);
        File.WriteAllText(path, json);
    }

    public static string GetSettingsPath()
    {
        var currentDirectoryPath = Path.Combine(Environment.CurrentDirectory, FileName);
        var projectPath = Path.Combine(Environment.CurrentDirectory, "RemotePC.csproj");
        if (File.Exists(currentDirectoryPath) || File.Exists(projectPath))
        {
            return currentDirectoryPath;
        }

        return Path.Combine(AppContext.BaseDirectory, FileName);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    private sealed class AppSettings
    {
        [JsonPropertyName("Supabase")]
        public SupabaseOptions? Supabase { get; set; }
    }
}
