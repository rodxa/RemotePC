using Microsoft.Win32;

namespace RemotePC.Services;

public sealed class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RemotePC";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsEnabled()
    {
        if (!IsSupported)
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        if (!IsSupported)
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{GetExecutablePath()}\" --background", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ??
               System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ??
               Path.Combine(AppContext.BaseDirectory, "RemotePC.exe");
    }
}
