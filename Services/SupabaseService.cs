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

    public async Task<IReadOnlyList<PcDevice>> GetPcsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = CreateRequest(
            HttpMethod.Get,
            "/rest/v1/pc_remote_control?select=id,device_name,display_name,command_id,tailscale_ip,rustdesk_id,enabled,last_seen,sort_order,updated_at,remote_port,remote_enabled,remote_device_id,remote_version&order=sort_order.asc,id.asc");

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

    public async Task<long> AddPcAsync(PcDeviceCreateRequest pc, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var payload = JsonSerializer.Serialize(
            new
            {
                p_device_name = pc.DeviceName,
                p_display_name = pc.DisplayName,
                p_tailscale_ip = pc.TailscaleIp,
                p_rustdesk_id = pc.RustDeskId
            },
            JsonOptions);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var request = CreateRequest(HttpMethod.Post, "/rest/v1/rpc/add_pc_device");
        request.Content = content;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SupabaseException($"Add PC failed with {(int)response.StatusCode}: {TrimBody(body)}");
        }

        try
        {
            return JsonSerializer.Deserialize<long>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new SupabaseException("Supabase added the PC but returned an invalid row id.", ex);
        }
    }

    public async Task UpdatePcAsync(long pcId, PcDeviceCreateRequest pc, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var payload = JsonSerializer.Serialize(
            new
            {
                p_id = pcId,
                p_device_name = pc.DeviceName,
                p_display_name = pc.DisplayName,
                p_tailscale_ip = pc.TailscaleIp,
                p_rustdesk_id = pc.RustDeskId,
                p_enabled = pc.Enabled
            },
            JsonOptions);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var request = CreateRequest(HttpMethod.Post, "/rest/v1/rpc/update_pc_device");
        request.Content = content;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SupabaseException($"Update PC failed with {(int)response.StatusCode}: {TrimBody(body)}");
        }
    }

    public async Task DeletePcAsync(long pcId, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var payload = JsonSerializer.Serialize(new { p_id = pcId }, JsonOptions);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var request = CreateRequest(HttpMethod.Post, "/rest/v1/rpc/delete_pc_device");
        request.Content = content;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SupabaseException($"Delete PC failed with {(int)response.StatusCode}: {TrimBody(body)}");
        }
    }

    public async Task<IReadOnlyList<PcCommand>> GetCommandsAsync(long pcId, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = CreateRequest(
            HttpMethod.Get,
            $"/rest/v1/pc_commands?select=id,pc_id,name,description,category,command_type,command,arguments,working_directory,require_confirmation,timeout_seconds,enabled,sort_order,created_at,updated_at&pc_id=eq.{pcId}&order=category.asc,sort_order.asc,id.asc");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SupabaseException($"Load actions failed with {(int)response.StatusCode}: {TrimBody(body)}");
        }

        try
        {
            return JsonSerializer.Deserialize<List<PcCommand>>(body, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new SupabaseException("Supabase returned malformed action data.", ex);
        }
    }

    public async Task<PcCommand?> GetCommandAsync(long actionId, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = CreateRequest(
            HttpMethod.Get,
            $"/rest/v1/pc_commands?select=id,pc_id,name,description,category,command_type,command,arguments,working_directory,require_confirmation,timeout_seconds,enabled,sort_order,created_at,updated_at&id=eq.{actionId}&limit=1");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SupabaseException($"Load action failed with {(int)response.StatusCode}: {TrimBody(body)}");
        }

        try
        {
            return JsonSerializer.Deserialize<List<PcCommand>>(body, JsonOptions)?.FirstOrDefault();
        }
        catch (JsonException ex)
        {
            throw new SupabaseException("Supabase returned malformed action data.", ex);
        }
    }

    public async Task<long> SaveCommandAsync(PcCommandSaveRequest action, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var payload = JsonSerializer.Serialize(
            new
            {
                pc_id = action.PcId,
                name = action.Name.Trim(),
                description = NullIfWhiteSpace(action.Description),
                category = NullIfWhiteSpace(action.Category),
                command_type = action.CommandType,
                command = NullIfWhiteSpace(action.Command),
                arguments = NullIfWhiteSpace(action.Arguments),
                working_directory = NullIfWhiteSpace(action.WorkingDirectory),
                require_confirmation = action.RequireConfirmation,
                timeout_seconds = action.TimeoutSeconds,
                enabled = action.Enabled,
                sort_order = action.SortOrder,
                updated_at = DateTimeOffset.UtcNow
            },
            JsonOptions);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var path = action.Id is { } id
            ? $"/rest/v1/pc_commands?id=eq.{id}"
            : "/rest/v1/pc_commands";
        using var request = CreateRequest(action.Id is null ? HttpMethod.Post : HttpMethod.Patch, path);
        request.Content = content;
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SupabaseException($"Save action failed with {(int)response.StatusCode}: {TrimBody(body)}");
        }

        try
        {
            var saved = JsonSerializer.Deserialize<List<PcCommand>>(body, JsonOptions)?.FirstOrDefault();
            return saved?.Id ?? action.Id ?? 0;
        }
        catch (JsonException ex)
        {
            throw new SupabaseException("Supabase saved the action but returned an invalid row.", ex);
        }
    }

    public async Task DeleteCommandAsync(long actionId, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = CreateRequest(HttpMethod.Delete, $"/rest/v1/pc_commands?id=eq.{actionId}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SupabaseException($"Delete action failed with {(int)response.StatusCode}: {TrimBody(body)}");
        }
    }

    public async Task UpdateRemoteMetadataAsync(
        long pcId,
        bool remoteEnabled,
        int remotePort,
        string remoteDeviceId,
        string? remoteVersion,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var payload = JsonSerializer.Serialize(
            new
            {
                p_id = pcId,
                p_remote_enabled = remoteEnabled,
                p_remote_port = remotePort,
                p_remote_device_id = remoteDeviceId,
                p_remote_version = remoteVersion
            },
            JsonOptions);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var request = CreateRequest(HttpMethod.Post, "/rest/v1/rpc/update_pc_remote_metadata");
        request.Content = content;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new SupabaseException($"Update remote metadata failed with {(int)response.StatusCode}: {TrimBody(body)}");
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

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
