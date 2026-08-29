using System.Diagnostics;

namespace RemotePC.Services;

public sealed class ParsecService
{
    private static readonly string[] RelativeExecutables =
    [
        "parsecd.exe",
        "parsec.exe"
    ];

    public Task LaunchAsync(string? parsecPeerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executable = FindParsecExecutable();
        if (executable is null)
        {
            throw new FileNotFoundException("Parsec was not found. Install Parsec or check its installation path.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
        };

        Process.Start(startInfo);
        return Task.CompletedTask;
    }

    private static string? FindParsecExecutable()
    {
        foreach (var directory in GetCandidateDirectories())
        {
            foreach (var executable in RelativeExecutables)
            {
                var path = Path.Combine(directory, executable);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateDirectories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Parsec");
            yield return Path.Combine(localAppData, "Programs", "Parsec");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Parsec");
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Parsec");
        }
    }
}
