using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;
using System.Net;

namespace RemotePC.Services;

public sealed class PcStatusService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan TailscaleTimeout = TimeSpan.FromSeconds(3);
    private const string TailscaleExecutableName = "tailscale.exe";

    public async Task<bool> IsReachableAsync(string? tailscaleIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tailscaleIp))
        {
            return false;
        }

        var normalizedIp = TailscaleIpAddress.Normalize(tailscaleIp);
        if (string.IsNullOrWhiteSpace(normalizedIp))
        {
            return false;
        }

        var tailscaleExecutable = FindTailscaleExecutable();
        if (tailscaleExecutable is not null)
        {
            var tailscaleResult = await IsReachableWithTailscaleAsync(tailscaleExecutable, normalizedIp, cancellationToken);
            if (tailscaleResult.HasValue)
            {
                return tailscaleResult.Value;
            }
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(normalizedIp, (int)DefaultTimeout.TotalMilliseconds)
                .WaitAsync(cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException or SocketException)
        {
            return false;
        }
    }

    public bool IsLocalTailscaleIp(string? tailscaleIp)
    {
        if (!IPAddress.TryParse(TailscaleIpAddress.Normalize(tailscaleIp), out var targetIp))
        {
            return false;
        }

        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(static networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Any(address => address.Address.Equals(targetIp));
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

    private static async Task<bool?> IsReachableWithTailscaleAsync(
        string tailscaleExecutable,
        string tailscaleIp,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TailscaleTimeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = tailscaleExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("ping");
        startInfo.ArgumentList.Add("--c");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--timeout");
        startInfo.ArgumentList.Add("2s");
        startInfo.ArgumentList.Add("--until-direct=false");
        startInfo.ArgumentList.Add(tailscaleIp);

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            var output = string.Concat(await outputTask, Environment.NewLine, await errorTask);

            return process.ExitCode == 0 || IsTailscalePingSuccess(output);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static bool IsTailscalePingSuccess(string output)
    {
        return output.Contains("pong from", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static string? FindTailscaleExecutable()
    {
        foreach (var directory in GetTailscaleCandidateDirectories())
        {
            var path = Path.Combine(directory, TailscaleExecutableName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetTailscaleCandidateDirectories()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Tailscale");
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Tailscale");
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            yield break;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return directory;
        }
    }
}
