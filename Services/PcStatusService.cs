using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RemotePC.Services;

public sealed class PcStatusService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(1500);

    public async Task<bool> IsReachableAsync(string? tailscaleIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tailscaleIp))
        {
            return false;
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(tailscaleIp.Trim(), (int)DefaultTimeout.TotalMilliseconds)
                .WaitAsync(cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException or SocketException)
        {
            return false;
        }
    }

    public async Task<bool> WaitUntilReachableAsync(
        string? tailscaleIp,
        TimeSpan timeout,
        TimeSpan pollInterval,
        IProgress<TimeSpan>? remainingProgress,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsReachableAsync(tailscaleIp, cancellationToken))
            {
                return true;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            remainingProgress?.Report(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);

            var delay = remaining < pollInterval ? remaining : pollInterval;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        return false;
    }
}
