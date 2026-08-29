using System.Collections.Concurrent;
using System.Diagnostics;

namespace RemotePC.Services;

public sealed class RustDeskService
{
    private const string ExecutableName = "rustdesk.exe";
    private static readonly TimeSpan HelpProbeTimeout = TimeSpan.FromSeconds(3);
    private readonly ConcurrentDictionary<string, Lazy<Task<DirectConnectSyntax?>>> _directConnectSyntaxCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task LaunchAsync(string? rustDeskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(rustDeskId))
        {
            throw new InvalidOperationException("RustDesk ID not configured");
        }

        var executable = FindRustDeskExecutable();
        if (executable is null)
        {
            throw new FileNotFoundException("RustDesk is not installed. Install RustDesk or check its installation path.");
        }

        var startInfo = CreateStartInfo(executable);
        var directConnectSyntax = await GetDirectConnectSyntaxAsync(executable, cancellationToken);
        if (directConnectSyntax is not null)
        {
            directConnectSyntax.AddArguments(startInfo, rustDeskId.Trim());
        }

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("RustDesk could not be started.");
        }
    }

    private Task<DirectConnectSyntax?> GetDirectConnectSyntaxAsync(string executable, CancellationToken cancellationToken)
    {
        var lazy = _directConnectSyntaxCache.GetOrAdd(
            executable,
            static path => new Lazy<Task<DirectConnectSyntax?>>(() => ProbeDirectConnectSyntaxAsync(path)));

        return lazy.Value.WaitAsync(cancellationToken);
    }

    private static async Task<DirectConnectSyntax?> ProbeDirectConnectSyntaxAsync(string executable)
    {
        using var cts = new CancellationTokenSource(HelpProbeTimeout);
        using var process = StartHelpProbe(executable);
        if (process is null)
        {
            return null;
        }

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            var helpText = string.Concat(await outputTask, Environment.NewLine, await errorTask);
            return ParseDirectConnectSyntax(helpText);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static Process? StartHelpProbe(string executable)
    {
        var startInfo = CreateStartInfo(executable);
        startInfo.ArgumentList.Add("--help");
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
        startInfo.UseShellExecute = false;

        return Process.Start(startInfo);
    }

    private static DirectConnectSyntax? ParseDirectConnectSyntax(string helpText)
    {
        if (helpText.Contains("--connect", StringComparison.OrdinalIgnoreCase))
        {
            return DirectConnectSyntax.LongConnectOption;
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executable)
    {
        return new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };
    }

    private static string? FindRustDeskExecutable()
    {
        foreach (var directory in GetCandidateDirectories())
        {
            var path = Path.Combine(directory, ExecutableName);
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
            yield return Path.Combine(programFiles, "RustDesk");
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "RustDesk");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "RustDesk");
            yield return Path.Combine(localAppData, "RustDesk");
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

    private sealed class DirectConnectSyntax
    {
        public static readonly DirectConnectSyntax LongConnectOption = new("--connect");

        private readonly string _option;

        private DirectConnectSyntax(string option)
        {
            _option = option;
        }

        public void AddArguments(ProcessStartInfo startInfo, string rustDeskId)
        {
            startInfo.ArgumentList.Add(_option);
            startInfo.ArgumentList.Add(rustDeskId);
        }
    }
}
