using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RemotePC.Models;

namespace RemotePC.Services;

public sealed class RemoteHostClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient _httpClient;
    private readonly ProtectedCredentialStore _credentials;

    public RemoteHostClient(HttpClient httpClient, ProtectedCredentialStore credentials)
    {
        _httpClient = httpClient;
        _credentials = credentials;
    }

    public async Task<RemoteHostHealth?> GetHealthAsync(PcDevice device, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.TailscaleHost))
        {
            return null;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(HealthTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, CreateUri(device, "/api/health"));

        try
        {
            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return JsonSerializer.Deserialize<RemoteHostHealth>(body, JsonOptions);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    public Task<ActionExecutionResult> ShutdownAsync(PcDevice device, bool confirmed, string password, CancellationToken cancellationToken)
    {
        return PostCommandAsync(device, "/api/builtin/shutdown", new RemoteActionRequest { Confirmed = confirmed }, password, cancellationToken);
    }

    public Task<ActionExecutionResult> RestartAsync(PcDevice device, bool confirmed, string password, CancellationToken cancellationToken)
    {
        return PostCommandAsync(device, "/api/builtin/restart", new RemoteActionRequest { Confirmed = confirmed }, password, cancellationToken);
    }

    public Task<ActionExecutionResult> LockAsync(PcDevice device, string password, CancellationToken cancellationToken)
    {
        return PostCommandAsync(device, "/api/builtin/lock", new RemoteActionRequest { Confirmed = true }, password, cancellationToken);
    }

    public Task<ActionExecutionResult> ExecuteActionAsync(PcDevice device, long actionId, bool confirmed, string password, CancellationToken cancellationToken)
    {
        return PostCommandAsync(device, $"/api/actions/{actionId}", new RemoteActionRequest { Confirmed = confirmed }, password, cancellationToken);
    }

    private async Task<RemoteAuthorizationResult> GetTemporaryTokenAsync(PcDevice device, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.TailscaleHost))
        {
            return RemoteAuthorizationResult.Failed("This PC has no Tailscale IP configured in Supabase.");
        }

        var health = await GetHealthAsync(device, cancellationToken);
        if (health is null)
        {
            return RemoteAuthorizationResult.Failed(
                $"RemotePC host is not reachable at {CreateUri(device, "/api/health")}. Check that host mode is enabled, RemotePC is running on the target Windows user session, Tailscale is connected, and the firewall allows the host port.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(AuthorizationTimeout);

        try
        {
            var payload = JsonSerializer.Serialize(
                new RemotePasswordRequest
                {
                    Password = password,
                    ClientDeviceId = _credentials.GetOrCreateLocalDeviceId()
                },
                JsonOptions);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(CreateUri(device, "/api/auth/token"), content, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return RemoteAuthorizationResult.Failed("Wrong Remote Command password.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return RemoteAuthorizationResult.Failed($"Authorization failed with {(int)response.StatusCode}: {TrimBody(body)}");
            }

            var authorization = JsonSerializer.Deserialize<RemotePasswordResponse>(body, JsonOptions);
            if (string.IsNullOrWhiteSpace(authorization?.Token))
            {
                return RemoteAuthorizationResult.Failed("Host returned an empty authorization token.");
            }

            return RemoteAuthorizationResult.Succeeded(authorization.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RemoteAuthorizationResult.Failed("Authorization timed out. The PC may be reachable, but the RemotePC host port is blocked or not listening.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return RemoteAuthorizationResult.Failed($"Authorization failed: {ex.Message}");
        }
    }

    private async Task<ActionExecutionResult> PostCommandAsync<T>(
        PcDevice device,
        string path,
        T payload,
        string password,
        CancellationToken cancellationToken)
    {
        var authorization = await GetTemporaryTokenAsync(device, password, cancellationToken);
        if (!authorization.Success || string.IsNullOrWhiteSpace(authorization.Token))
        {
            return ActionExecutionResult.Failed(authorization.Message);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CommandTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, CreateUri(device, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorization.Token);
        request.Headers.TryAddWithoutValidation("X-RemotePC-DeviceId", _credentials.GetOrCreateLocalDeviceId());
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return ActionExecutionResult.Failed("The host rejected this authorization token.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return ActionExecutionResult.Failed($"Host returned {(int)response.StatusCode}: {TrimBody(body)}");
            }

            return JsonSerializer.Deserialize<ActionExecutionResult>(body, JsonOptions) ??
                   ActionExecutionResult.Failed("Host returned an empty result.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ActionExecutionResult.Failed($"RemotePC host unavailable: {ex.Message}");
        }
    }

    private static Uri CreateUri(PcDevice device, string path)
    {
        var port = device.RemotePort is > 0 and <= 65535 ? device.RemotePort : LocalAppOptions.DefaultRemotePort;
        return new UriBuilder(Uri.UriSchemeHttp, device.TailscaleHost, port, path.TrimStart('/')).Uri;
    }

    private static string TrimBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty response";
        }

        return body.Length <= 240 ? body : string.Concat(body.AsSpan(0, 240), "...");
    }
}

public sealed class RemoteAuthorizationResult
{
    private RemoteAuthorizationResult(bool success, string message, string? token)
    {
        Success = success;
        Message = message;
        Token = token;
    }

    public bool Success { get; }

    public string Message { get; }

    public string? Token { get; }

    public static RemoteAuthorizationResult Succeeded(string token)
    {
        return new RemoteAuthorizationResult(true, "Authorization succeeded.", token);
    }

    public static RemoteAuthorizationResult Failed(string message)
    {
        return new RemoteAuthorizationResult(false, message, null);
    }
}
