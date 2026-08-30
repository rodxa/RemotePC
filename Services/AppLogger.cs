using System.Diagnostics;

namespace RemotePC.Services;

public sealed class AppLogger
{
    private readonly object _sync = new();

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Warn(string message)
    {
        Write("WARN", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message}: {exception.Message}");
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:u} [{level}] {message}";
        lock (_sync)
        {
            Debug.WriteLine(line);
            Trace.WriteLine(line);
        }
    }
}
