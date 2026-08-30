using System.Text.RegularExpressions;
using RemotePC.Models;

namespace RemotePC.Services;

public sealed class ActionSafetyService
{
    private static readonly Regex DeleteCommandRegex = new(
        @"(?i)(^|[\s;&|])(remove-item|rm|del|erase|rmdir|rd)(\s|$)",
        RegexOptions.Compiled);

    private static readonly Regex ProtectedPathRegex = new(
        @"(?i)(^|[\s'""])(c:\\($|[\s\\'""]|\*)|c:\\windows\b|c:\\program files\b|c:\\program files \(x86\)\b|c:\\users\b|%systemroot%|%windir%|\$env:systemroot|\$env:windir|system32\b)",
        RegexOptions.Compiled);

    private static readonly string[] AlwaysBlockedPowerShellFragments =
    [
        "format-volume",
        "clear-disk",
        "initialize-disk",
        "remove-partition",
        "reset-physicaldisk",
        "bcdedit",
        "manage-bde",
        "vssadmin delete",
        "wbadmin delete",
        "cipher /w",
        "stop-computer",
        "restart-computer",
        "invoke-expression",
        " iex ",
        "-encodedcommand",
        " -enc "
    ];

    private static readonly HashSet<string> BlockedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe",
        "powershell.exe",
        "pwsh.exe",
        "shutdown.exe",
        "format.com",
        "diskpart.exe",
        "bcdedit.exe",
        "reg.exe",
        "regedit.exe",
        "takeown.exe",
        "icacls.exe",
        "cipher.exe",
        "vssadmin.exe",
        "wbadmin.exe"
    };

    public ActionSafetyResult Validate(PcCommand action)
    {
        return Validate(action.CommandType, action.Command, action.Arguments);
    }

    public ActionSafetyResult Validate(PcCommandSaveRequest action)
    {
        return Validate(action.CommandType, action.Command, action.Arguments);
    }

    private static ActionSafetyResult Validate(string commandType, string? command, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return ActionSafetyResult.Allowed();
        }

        return commandType switch
        {
            PcCommandTypes.PowerShell => ValidatePowerShell(command),
            PcCommandTypes.Process => ValidateProcess(command, arguments),
            _ => ActionSafetyResult.Blocked("Unsupported action type.")
        };
    }

    private static ActionSafetyResult ValidatePowerShell(string command)
    {
        var padded = $" {command.Trim()} ";
        var lowered = padded.ToLowerInvariant();

        foreach (var fragment in AlwaysBlockedPowerShellFragments)
        {
            if (lowered.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return ActionSafetyResult.Blocked($"Blocked because it contains `{fragment.Trim()}`.");
            }
        }

        if (DeleteCommandRegex.IsMatch(command) && ProtectedPathRegex.IsMatch(command))
        {
            return ActionSafetyResult.Blocked("Blocked because it deletes or removes protected Windows/user/system paths.");
        }

        if (DeleteCommandRegex.IsMatch(command) &&
            lowered.Contains("-recurse", StringComparison.OrdinalIgnoreCase) &&
            (lowered.Contains(" *", StringComparison.OrdinalIgnoreCase) ||
             lowered.Contains(" .*", StringComparison.OrdinalIgnoreCase) ||
             lowered.Contains(" . ", StringComparison.OrdinalIgnoreCase)))
        {
            return ActionSafetyResult.Blocked("Blocked because recursive deletion with a broad relative target is too risky.");
        }

        return ActionSafetyResult.Allowed();
    }

    private static ActionSafetyResult ValidateProcess(string command, string? arguments)
    {
        var executableName = Path.GetFileName(command.Trim().Trim('"'));
        if (BlockedProcessNames.Contains(executableName))
        {
            return ActionSafetyResult.Blocked($"Blocked process action executable `{executableName}`. Use built-ins or safer saved actions instead.");
        }

        var combined = $"{command} {arguments}".Trim();
        if (ProtectedPathRegex.IsMatch(combined) &&
            combined.Contains("/s", StringComparison.OrdinalIgnoreCase) &&
            combined.Contains("/q", StringComparison.OrdinalIgnoreCase))
        {
            return ActionSafetyResult.Blocked("Blocked because the process arguments look like quiet recursive deletion of a protected path.");
        }

        return ActionSafetyResult.Allowed();
    }
}

public sealed class ActionSafetyResult
{
    private ActionSafetyResult(bool isAllowed, string? reason)
    {
        IsAllowed = isAllowed;
        Reason = reason;
    }

    public bool IsAllowed { get; }

    public string? Reason { get; }

    public static ActionSafetyResult Allowed()
    {
        return new ActionSafetyResult(true, null);
    }

    public static ActionSafetyResult Blocked(string reason)
    {
        return new ActionSafetyResult(false, reason);
    }
}
