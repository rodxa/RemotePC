using System.Diagnostics;

namespace RemotePC.Services;

public sealed class RustDeskService
{
    private const string ExecutableName = "rustdesk.exe";
    private const string RustDeskUriScheme = "rustdesk";

    public Task LaunchAsync(string? rustDeskId, CancellationToken cancellationToken)
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

        if (TryLaunchRustDeskUri(rustDeskId.Trim()))
        {
            return Task.CompletedTask;
        }

        var startInfo = CreateStartInfo(executable);
        startInfo.ArgumentList.Add("--connect");
        startInfo.ArgumentList.Add(rustDeskId.Trim());

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("RustDesk could not be started.");
        }

        return Task.CompletedTask;
    }

    private static bool TryLaunchRustDeskUri(string rustDeskId)
    {
        if (!IsRustDeskUriSchemeRegistered())
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = $"rustdesk://connection/new/{Uri.EscapeDataString(rustDeskId)}",
                UseShellExecute = true
            };

            return Process.Start(startInfo) is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool IsRustDeskUriSchemeRegistered()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{RustDeskUriScheme}\shell\open\command");
            return key?.GetValue(null) is string command && !string.IsNullOrWhiteSpace(command);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
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

}
