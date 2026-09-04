using System.Text.Json;
using System.Text.Json.Serialization;
using RemotePC.Models;

namespace RemotePC.Configuration;

public static class AppConfiguration
{
    private const string FileName = "appsettings.json";

    public static SupabaseOptions Load()
    {
        return LoadAll().Supabase.Validated();
    }

    public static AppSettings LoadAll()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            return new AppSettings
            {
                Supabase = SupabaseOptions.Missing($"Create {FileName} next to the executable and add your Supabase URL and publishable key."),
                Local = new LocalAppOptions().Normalized()
            };
        }

        try
        {
            var json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return new AppSettings
            {
                Supabase = (root?.Supabase ?? SupabaseOptions.Missing($"The {FileName} file does not contain a Supabase section.")).Validated(),
                Local = (root?.Local ?? new LocalAppOptions()).Normalized()
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings
            {
                Supabase = SupabaseOptions.Missing($"Could not read {FileName}: {ex.Message}"),
                Local = new LocalAppOptions().Normalized()
            };
        }
    }

    public static SupabaseOptions LoadForEditing()
    {
        return LoadAll().Supabase.IsConfigured ? LoadAll().Supabase : new SupabaseOptions();
    }

    public static void Save(SupabaseOptions options)
    {
        var current = LoadAll();
        SaveAll(new AppSettings
        {
            Supabase = options,
            Local = current.Local
        });
    }

    public static void SaveLocal(Func<LocalAppOptions, LocalAppOptions> update)
    {
        var current = LoadAll();
        SaveAll(new AppSettings
        {
            Supabase = current.Supabase,
            Local = update(current.Local.Normalized()).Normalized()
        });
    }

    public static bool TrySaveLocal(Func<LocalAppOptions, LocalAppOptions> update)
    {
        if (!File.Exists(GetSettingsPath()))
        {
            return false;
        }

        SaveLocal(update);
        return true;
    }

    public static void SaveAll(AppSettings settings)
    {
        var path = GetSettingsPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var saved = new AppSettings
        {
            Supabase = new SupabaseOptions
            {
                Url = settings.Supabase.Url.Trim(),
                PublishableKey = settings.Supabase.PublishableKey.Trim()
            },
            Local = settings.Local.Normalized()
        };

        var json = JsonSerializer.Serialize(saved, WriteOptions);
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

    public sealed class AppSettings
    {
        [JsonPropertyName("Supabase")]
        public SupabaseOptions Supabase { get; set; } = new();

        [JsonPropertyName("Local")]
        public LocalAppOptions Local { get; set; } = new();
    }
}
