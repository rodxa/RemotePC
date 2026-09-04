using RemotePC.Models;

namespace RemotePC.Services;

public sealed class RemoteSessionState
{
    private static readonly TimeSpan ControllerSessionTimeout = TimeSpan.FromHours(8);
    private readonly object _sync = new();
    private string? _controllerDeviceId;
    private string? _controllerRustDeskId;
    private DateTimeOffset _controllerExpiresAtUtc;

    public void MarkController(RemoteControllerSessionRequest request)
    {
        var deviceId = NormalizeDeviceId(request.ClientDeviceId);
        var rustDeskId = RustDeskService.NormalizeRustDeskId(request.ClientRustDeskId);
        if (string.IsNullOrWhiteSpace(deviceId) && string.IsNullOrWhiteSpace(rustDeskId))
        {
            return;
        }

        lock (_sync)
        {
            _controllerDeviceId = deviceId;
            _controllerRustDeskId = rustDeskId;
            _controllerExpiresAtUtc = DateTimeOffset.UtcNow + ControllerSessionTimeout;
        }
    }

    public bool IsCurrentController(Guid? remoteDeviceId, string? rustDeskId)
    {
        var normalizedDeviceId = remoteDeviceId?.ToString("D");
        var normalizedRustDeskId = RustDeskService.NormalizeRustDeskId(rustDeskId);

        lock (_sync)
        {
            if (DateTimeOffset.UtcNow >= _controllerExpiresAtUtc)
            {
                _controllerDeviceId = null;
                _controllerRustDeskId = null;
                _controllerExpiresAtUtc = default;
                return false;
            }

            return (!string.IsNullOrWhiteSpace(normalizedDeviceId) &&
                    string.Equals(_controllerDeviceId, normalizedDeviceId, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(normalizedRustDeskId) &&
                    string.Equals(_controllerRustDeskId, normalizedRustDeskId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string? NormalizeDeviceId(string? deviceId)
    {
        return Guid.TryParse(deviceId, out var parsed)
            ? parsed.ToString("D")
            : null;
    }
}
