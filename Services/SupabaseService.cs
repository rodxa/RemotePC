using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RemotePC.Configuration;
using RemotePC.Models;

namespace RemotePC.Services;

public sealed class SupabaseService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private SupabaseOptions _options;
    private bool _disposed;

    public SupabaseService(HttpClient httpClient, SupabaseOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public void ReloadConfiguration()
    {
        _options = AppConfiguration.Load();
    }

    public async Task<IReadOnlyList<PcDevice>> GetEnabledPcsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = CreateRequest(
            HttpMethod.Get,
            "/rest/v1/pc_remote_control?select=id,device_name,display_name,command_id,tailscale_ip,rustdesk_id,enabled,last_seen,sort_order,updated_at&enabled=eq.true&order=sort_order.asc,id.asc");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SupabaseException($"Supabase returned {(int)response.StatusCode}: {TrimBody(body)}");
        }

        try
        {
            return JsonSerializer.Deserialize<List<PcDevice>>(body, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new SupabaseException("Supabase returned a malformed PC list.", ex);
        }
    }

    public async Task WakePcAsync(long pcId, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var payload = JsonSerializer.Serialize(new { target_id = pcId }, JsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var request = CreateRequest(HttpMethod.Post, "/rest/v1/rpc/wake_pc");
        request.Content = content;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SupabaseException($"Wake command failed with {(int)response.StatusCode}: {TrimBody(body)}");
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(_options.Url), relativePath));
        request.Headers.TryAddWithoutValidation("apikey", _options.PublishableKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private void EnsureConfigured()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_options.IsConfigured)
        {
            throw new SupabaseException(_options.ConfigurationError ?? "Supabase configuration is missing.");
        }
    }

    private static string TrimBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty response";
        }

        return body.Length <= 240 ? body : string.Concat(body.AsSpan(0, 240), "...");
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
