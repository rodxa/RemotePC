using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using RemotePC.Configuration;
using RemotePC.Models;

namespace RemotePC.Services;

public sealed class RemoteHostServer : IAsyncDisposable
{
    private readonly SupabaseService _supabase;
    private readonly ProtectedCredentialStore _credentials;
    private readonly ActionExecutor _executor;
    private readonly ActionSafetyService _safety;
    private readonly TailscaleService _tailscale;
    private readonly AppLogger _logger;
    private WebApplication? _app;
    private DateTimeOffset _startedAt;
    private LocalAppOptions _options = new();

    public RemoteHostServer(
        SupabaseService supabase,
        ProtectedCredentialStore credentials,
        ActionExecutor executor,
        TailscaleService tailscale,
        AppLogger logger)
    {
        _supabase = supabase;
        _credentials = credentials;
        _executor = executor;
        _safety = new ActionSafetyService();
        _tailscale = tailscale;
        _logger = logger;
    }

    public bool IsRunning => _app is not null;

    public async Task ApplySettingsAsync(LocalAppOptions options, CancellationToken cancellationToken)
    {
        options = options.Normalized();
        if (!options.RemoteControlEnabled)
        {
            await StopAsync(cancellationToken);
            return;
        }

        if (_app is not null && _options.RemotePort == options.RemotePort)
        {
            _options = options;
            return;
        }

        await StopAsync(cancellationToken);
        await StartAsync(options, cancellationToken);
    }

    public async Task StartAsync(LocalAppOptions options, CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            return;
        }

        _options = options.Normalized();
        _startedAt = DateTimeOffset.UtcNow;
        _credentials.GetOrCreateHostToken();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            var localTailscaleIp = _tailscale.GetLocalTailscaleIp();
            if (IPAddress.TryParse(localTailscaleIp, out var tailscaleAddress))
            {
                kestrel.Listen(tailscaleAddress, _options.RemotePort);
                _logger.Info($"Host binding to Tailscale IP {tailscaleAddress}:{_options.RemotePort}");
            }
            else
            {
                kestrel.Listen(IPAddress.Loopback, _options.RemotePort);
                _logger.Warn($"Tailscale IP was not detectable; host bound to loopback:{_options.RemotePort}");
            }
        });

        _app = builder.Build();
        MapRoutes(_app);

        await _app.StartAsync(cancellationToken);
        _logger.Info("RemotePC host started");
        _ = TryPublishHostMetadataAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_app is null)
        {
            return;
        }

        var app = _app;
        _app = null;
        await app.StopAsync(cancellationToken);
        await app.DisposeAsync();
        _logger.Info("RemotePC host stopped");
    }

    private void MapRoutes(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/health", () => new RemoteHostHealth
        {
            MachineName = _options.MachineName,
            Version = typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            HostEnabled = true,
            UptimeSeconds = (long)(DateTimeOffset.UtcNow - _startedAt).TotalSeconds
        });

        routes.MapPost("/api/auth/token", (RemotePasswordRequest request) =>
        {
            if (!_credentials.VerifyHostPassword(request.Password))
            {
                _logger.Warn("Password authorization rejected");
                return Results.Unauthorized();
            }

            _logger.Info("Password authorization succeeded");
            return Results.Ok(new RemotePasswordResponse
            {
                HostDeviceId = _credentials.GetOrCreateLocalDeviceId(),
                Token = _credentials.GetOrCreateHostToken()
            });
        });

        routes.MapPost("/api/builtin/shutdown", async (HttpContext context, RemoteActionRequest request, CancellationToken cancellationToken) =>
        {
            if (!IsAuthenticated(context))
            {
                return Results.Unauthorized();
            }

            if (!request.Confirmed)
            {
                return Results.BadRequest(ActionExecutionResult.Failed("Shutdown requires confirmation."));
            }

            _logger.Warn("Remote shutdown requested");
            return Results.Ok(await _executor.ShutdownAsync(restart: false, cancellationToken));
        });

        routes.MapPost("/api/builtin/restart", async (HttpContext context, RemoteActionRequest request, CancellationToken cancellationToken) =>
        {
            if (!IsAuthenticated(context))
            {
                return Results.Unauthorized();
            }

            if (!request.Confirmed)
            {
                return Results.BadRequest(ActionExecutionResult.Failed("Restart requires confirmation."));
            }

            _logger.Warn("Remote restart requested");
            return Results.Ok(await _executor.ShutdownAsync(restart: true, cancellationToken));
        });

        routes.MapPost("/api/builtin/lock", (HttpContext context) =>
        {
            if (!IsAuthenticated(context))
            {
                return Results.Unauthorized();
            }

            _logger.Info("Remote lock requested");
            return Results.Ok(_executor.Lock());
        });

        routes.MapPost("/api/actions/{id:long}", async (long id, HttpContext context, RemoteActionRequest request, CancellationToken cancellationToken) =>
        {
            if (!IsAuthenticated(context))
            {
                _logger.Warn("Remote action rejected");
                return Results.Unauthorized();
            }

            try
            {
                var localPc = await FindLocalPcAsync(cancellationToken);
                if (localPc is null)
                {
                    return Results.BadRequest(ActionExecutionResult.Failed("This RemotePC host is not matched to a Supabase PC row."));
                }

                var action = await _supabase.GetCommandAsync(id, cancellationToken);
                if (action is null || action.PcId != localPc.Id)
                {
                    return Results.NotFound(ActionExecutionResult.Failed("Action was not found for this machine."));
                }

                if (!action.Enabled)
                {
                    return Results.BadRequest(ActionExecutionResult.Failed("Action is disabled."));
                }

                if (action.RequireConfirmation && !request.Confirmed)
                {
                    return Results.BadRequest(ActionExecutionResult.Failed("Action requires confirmation."));
                }

                var safety = _safety.Validate(action);
                if (!safety.IsAllowed)
                {
                    _logger.Warn($"Action blocked by safety policy: {action.Id}");
                    return Results.BadRequest(ActionExecutionResult.Failed(safety.Reason ?? "Action blocked by safety policy."));
                }

                _logger.Info($"Action started: {action.Id}");
                var result = action.CommandType.ToLowerInvariant() switch
                {
                    PcCommandTypes.PowerShell => await _executor.ExecutePowerShellAsync(action, cancellationToken),
                    PcCommandTypes.Process => await _executor.ExecuteProcessAsync(action, cancellationToken),
                    _ => ActionExecutionResult.Failed("Unsupported action type.")
                };
                _logger.Info($"Action completed: {action.Id}; success={result.Success}; exitCode={result.ExitCode}");
                return Results.Ok(result);
            }
            catch (Exception ex) when (ex is SupabaseException or HttpRequestException or TaskCanceledException)
            {
                _logger.Error("Action failed before execution", ex);
                return Results.BadRequest(ActionExecutionResult.Failed(ex.Message));
            }
        });
    }

    private bool IsAuthenticated(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suppliedToken = header[prefix.Length..].Trim();
        var expectedToken = _credentials.GetOrCreateHostToken();
        var supplied = Encoding.UTF8.GetBytes(suppliedToken);
        var expected = Encoding.UTF8.GetBytes(expectedToken);
        return supplied.Length == expected.Length &&
               CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private async Task<PcDevice?> FindLocalPcAsync(CancellationToken cancellationToken)
    {
        var localDeviceId = _credentials.GetOrCreateLocalDeviceId();
        var localTailscaleIp = _tailscale.GetLocalTailscaleIp();
        var devices = await _supabase.GetPcsAsync(cancellationToken);
        return devices.FirstOrDefault(device =>
                   string.Equals(device.RemoteDeviceId?.ToString("D"), localDeviceId, StringComparison.OrdinalIgnoreCase)) ??
               devices.FirstOrDefault(device =>
                   !string.IsNullOrWhiteSpace(localTailscaleIp) &&
                   string.Equals(device.TailscaleIp, localTailscaleIp, StringComparison.OrdinalIgnoreCase));
    }

    private async Task TryPublishHostMetadataAsync(CancellationToken cancellationToken)
    {
        try
        {
            var localPc = await FindLocalPcAsync(cancellationToken);
            if (localPc is null)
            {
                return;
            }

            await _supabase.UpdateRemoteMetadataAsync(
                localPc.Id,
                remoteEnabled: true,
                _options.RemotePort,
                _credentials.GetOrCreateLocalDeviceId(),
                typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                cancellationToken);
        }
        catch (Exception ex) when (ex is SupabaseException or HttpRequestException or TaskCanceledException)
        {
            _logger.Warn($"Host metadata was not published: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await StopAsync(CancellationToken.None);
        }
    }
}
