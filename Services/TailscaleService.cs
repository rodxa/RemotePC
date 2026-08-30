using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace RemotePC.Services;

public sealed class TailscaleService
{
    private const string TailscaleExecutableName = "tailscale.exe";

    public string? GetLocalTailscaleIp()
    {
        var fromInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(static adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(static adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(static address => address.Address)
            .FirstOrDefault(static address =>
                address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                address.ToString().StartsWith("100.", StringComparison.Ordinal));

        if (fromInterfaces is not null)
        {
            return fromInterfaces.ToString();
        }

        return TryGetTailscaleIpFromCli();
    }

    private static string? TryGetTailscaleIpFromCli()
    {
        var executable = FindTailscaleExecutable();
        if (executable is null)
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("status");
            startInfo.ArgumentList.Add("--json");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1500);
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.TryGetProperty("Self", out var self) &&
                self.TryGetProperty("TailscaleIPs", out var ips))
            {
                foreach (var ip in ips.EnumerateArray())
                {
                    var value = ip.GetString();
                    if (IPAddress.TryParse(value, out var parsed) &&
                        parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return parsed.ToString();
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? FindTailscaleExecutable()
    {
        foreach (var directory in GetCandidateDirectories())
        {
            var path = Path.Combine(directory, TailscaleExecutableName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateDirectories()
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
