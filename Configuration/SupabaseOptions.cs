using System.Text.Json.Serialization;

namespace RemotePC.Configuration;

public sealed class SupabaseOptions
{
    [JsonPropertyName("Url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("PublishableKey")]
    public string PublishableKey { get; init; } = string.Empty;

    [JsonIgnore]
    public bool IsConfigured { get; private init; }

    [JsonIgnore]
    public string? ConfigurationError { get; private init; }

    public static SupabaseOptions Missing(string message)
    {
        return new SupabaseOptions { ConfigurationError = message };
    }

    public SupabaseOptions Validated()
    {
        if (string.IsNullOrWhiteSpace(Url) || Url.Contains("YOUR_PROJECT", StringComparison.OrdinalIgnoreCase))
        {
            return Missing("Set Supabase:Url in appsettings.json.");
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return Missing("Supabase:Url must be a valid Supabase project URL.");
        }

        if (string.IsNullOrWhiteSpace(PublishableKey) ||
            PublishableKey.Contains("YOUR_PUBLISHABLE_KEY", StringComparison.OrdinalIgnoreCase))
        {
            return Missing("Set Supabase:PublishableKey in appsettings.json.");
        }

        return new SupabaseOptions
        {
            Url = Url.TrimEnd('/'),
            PublishableKey = PublishableKey.Trim(),
            IsConfigured = true
        };
    }
}
