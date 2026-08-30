using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RemotePC.Models;

namespace RemotePC.Services;

public sealed class ActionExecutor
{
    private const int MaxOutputChars = 24_000;

    public async Task<ActionExecutionResult> ExecutePowerShellAsync(PcCommand action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.Command))
        {
            return ActionExecutionResult.Failed("PowerShell command is empty.");
        }

        var executable = FindExecutable("pwsh.exe") ?? FindExecutable("powershell.exe") ?? "powershell.exe";
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (!string.IsNullOrWhiteSpace(action.WorkingDirectory))
        {
            startInfo.WorkingDirectory = action.WorkingDirectory;
        }

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(action.Command);

        return await RunProcessAsync(startInfo, action.TimeoutSeconds, cancellationToken);
    }

    public Task<ActionExecutionResult> ExecuteProcessAsync(PcCommand action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(action.Command))
        {
            return Task.FromResult(ActionExecutionResult.Failed("Executable path is empty."));
        }

        if (!File.Exists(action.Command))
        {
            return Task.FromResult(ActionExecutionResult.Failed("Executable was not found."));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = action.Command,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        if (!string.IsNullOrWhiteSpace(action.Arguments))
        {
            foreach (var argument in SplitCommandLine(action.Arguments))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        if (!string.IsNullOrWhiteSpace(action.WorkingDirectory))
        {
            startInfo.WorkingDirectory = action.WorkingDirectory;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(startInfo);
            stopwatch.Stop();
            return Task.FromResult(process is null
                ? ActionExecutionResult.Failed("Process could not be started.")
                : new ActionExecutionResult
                {
                    Success = true,
                    ExitCode = 0,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    Message = "Process launched."
                });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            stopwatch.Stop();
            return Task.FromResult(new ActionExecutionResult
            {
                Success = false,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = ex.Message
            });
        }
    }

    public async Task<ActionExecutionResult> ShutdownAsync(bool restart, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(restart ? "/r" : "/s");
        startInfo.ArgumentList.Add("/t");
        startInfo.ArgumentList.Add("0");

        return await RunProcessAsync(startInfo, 10, cancellationToken);
    }

    public ActionExecutionResult Lock()
    {
        if (!OperatingSystem.IsWindows())
        {
            return ActionExecutionResult.Failed("Lock is only supported on Windows.");
        }

        var stopwatch = Stopwatch.StartNew();
        var success = LockWorkStation();
        stopwatch.Stop();

        return new ActionExecutionResult
        {
            Success = success,
            ExitCode = success ? 0 : 1,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Message = success ? "Workstation locked." : "Windows refused the lock request."
        };
    }

    private static async Task<ActionExecutionResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 3600));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        Process? process = null;

        try
        {
            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => AppendLimited(stdout, e.Data);
            process.ErrorDataReceived += (_, e) => AppendLimited(stderr, e.Data);

            if (!process.Start())
            {
                return ActionExecutionResult.Failed("Process could not be started.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(timeoutCts.Token);
            stopwatch.Stop();

            return new ActionExecutionResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Stdout = stdout.ToString(),
                Stderr = stderr.ToString()
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            stopwatch.Stop();
            return new ActionExecutionResult
            {
                Success = false,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Stdout = stdout.ToString(),
                Stderr = stderr.ToString(),
                Message = "Action timed out."
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            stopwatch.Stop();
            return new ActionExecutionResult
            {
                Success = false,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Stdout = stdout.ToString(),
                Stderr = stderr.ToString(),
                Message = ex.Message
            };
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void AppendLimited(StringBuilder builder, string? line)
    {
        if (line is null || builder.Length >= MaxOutputChars)
        {
            return;
        }

        var remaining = MaxOutputChars - builder.Length;
        if (line.Length + Environment.NewLine.Length <= remaining)
        {
            builder.AppendLine(line);
        }
        else
        {
            builder.AppendLine(line[..Math.Max(0, remaining - Environment.NewLine.Length)]);
        }
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
        catch
        {
        }
    }

    private static string? FindExecutable(string name)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitCommandLine(string commandLine)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();
}
