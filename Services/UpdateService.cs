using RemotePC.Configuration;
using RemotePC.Models;
using Velopack;
using Velopack.Sources;
using System.Net;

namespace RemotePC.Services;

public sealed class UpdateService
{
    public const string AppName = "RemotePC";
    public const string DefaultRepositoryUrl = "https://github.com/rodxa/RemotePC";
    public const string RepositoryUrlEnvironmentVariable = "REMOTEPC_GITHUB_REPOSITORY_URL";
    public const string PrereleaseEnvironmentVariable = "REMOTEPC_GITHUB_PRERELEASE";
    public const string Channel = "win-x64";

    private static readonly SemaphoreSlim CheckGate = new(1, 1);
    private readonly AppLogger _logger;

    public UpdateService(AppLogger logger)
    {
        _logger = logger;
    }

    public async Task<UpdateCheckResult> CheckDownloadApplyAndRestartAsync(
        string[]? restartArgs,
        CancellationToken cancellationToken)
    {
        if (!await CheckGate.WaitAsync(0, cancellationToken))
        {
            const string message = "Update check already in progress.";
            _logger.Info(message);
            return UpdateCheckResult.InProgress(message);
        }

        try
        {
            var repositoryUrl = GetRepositoryUrl();
            var prerelease = IsPrereleaseEnabled();
            var manager = CreateUpdateManager(repositoryUrl, prerelease);

            if (!manager.IsInstalled)
            {
                const string message = "Updates unavailable in development or unpacked builds.";
                _logger.Info(message);
                SaveUpdateStatus(message);
                return UpdateCheckResult.Unavailable(message);
            }

            SaveLastChecked("Checking GitHub Releases for updates...");
            _logger.Info($"Checking updates from {repositoryUrl} on channel {Channel}.");

            var updateInfo = await manager.CheckForUpdatesAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (updateInfo is null)
            {
                const string message = "No update available";
                SaveLastChecked(message);
                _logger.Info(message);
                return UpdateCheckResult.NoUpdate(message);
            }

            var version = updateInfo.TargetFullRelease.Version.ToString();
            var availableMessage = $"Update {version} available. Downloading...";
            SaveLastChecked(availableMessage);
            _logger.Info(availableMessage);

            await manager.DownloadUpdatesAsync(updateInfo, LogProgress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            SaveLastInstalled($"Update {version} downloaded. Restarting to install...");
            _logger.Info($"Applying update {version} and restarting.");

            manager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease, restartArgs);
            return UpdateCheckResult.RestartRequested($"Restarting to install update {version}.");
        }
        catch (OperationCanceledException)
        {
            const string message = "Update check canceled.";
            _logger.Info(message);
            SaveUpdateStatus(message);
            return UpdateCheckResult.Unavailable(message);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.Message.Contains("404", StringComparison.OrdinalIgnoreCase))
        {
            var message = $"No published update release found at {GetRepositoryUrl()} yet.";
            _logger.Warn(message);
            SaveUpdateStatus(message);
            return UpdateCheckResult.Unavailable(message);
        }
        catch (Exception ex)
        {
            var message = $"Updates unavailable: {ex.Message}";
            _logger.Warn(message);
            SaveUpdateStatus(message);
            return UpdateCheckResult.Unavailable(message);
        }
        finally
        {
            CheckGate.Release();
        }
    }

    private static UpdateManager CreateUpdateManager(string repositoryUrl, bool prerelease)
    {
        var source = new GithubSource(repositoryUrl, accessToken: null, prerelease: prerelease);
        var options = new UpdateOptions
        {
            ExplicitChannel = Channel
        };

        return new UpdateManager(source, options);
    }

    private static string GetRepositoryUrl()
    {
        var configured = Environment.GetEnvironmentVariable(RepositoryUrlEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? DefaultRepositoryUrl
            : configured.Trim();
    }

    private static bool IsPrereleaseEnabled()
    {
        var configured = Environment.GetEnvironmentVariable(PrereleaseEnvironmentVariable);
        return bool.TryParse(configured, out var prerelease) && prerelease;
    }

    private void LogProgress(int progress)
    {
        if (progress % 25 == 0 || progress == 100)
        {
            _logger.Info($"Update download progress: {progress}%.");
        }
    }

    private static void SaveLastChecked(string status)
    {
        var now = DateTimeOffset.UtcNow;
        AppConfiguration.TrySaveLocal(local => CopyWithUpdateState(
            local,
            lastCheckedUtc: now,
            lastInstalledUtc: local.LastUpdateInstalledUtc,
            status: status));
    }

    private static void SaveLastInstalled(string status)
    {
        var now = DateTimeOffset.UtcNow;
        AppConfiguration.TrySaveLocal(local => CopyWithUpdateState(
            local,
            lastCheckedUtc: local.LastUpdateCheckedUtc ?? now,
            lastInstalledUtc: now,
            status: status));
    }

    private static void SaveUpdateStatus(string status)
    {
        AppConfiguration.TrySaveLocal(local => CopyWithUpdateState(
            local,
            lastCheckedUtc: local.LastUpdateCheckedUtc,
            lastInstalledUtc: local.LastUpdateInstalledUtc,
            status: status));
    }

    private static LocalAppOptions CopyWithUpdateState(
        LocalAppOptions local,
        DateTimeOffset? lastCheckedUtc,
        DateTimeOffset? lastInstalledUtc,
        string status)
    {
        return new LocalAppOptions
        {
            StartWithWindows = local.StartWithWindows,
            StartMinimized = local.StartMinimized,
            CloseToTray = local.CloseToTray,
            RemoteControlEnabled = local.RemoteControlEnabled,
            NotificationsEnabled = local.NotificationsEnabled,
            MachineName = local.MachineName,
            RemotePort = local.RemotePort,
            LastUpdateCheckedUtc = lastCheckedUtc,
            LastUpdateInstalledUtc = lastInstalledUtc,
            LastUpdateStatus = status
        };
    }
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string Message)
{
    public static UpdateCheckResult NoUpdate(string message)
    {
        return new UpdateCheckResult(UpdateCheckStatus.NoUpdate, message);
    }

    public static UpdateCheckResult Unavailable(string message)
    {
        return new UpdateCheckResult(UpdateCheckStatus.Unavailable, message);
    }

    public static UpdateCheckResult InProgress(string message)
    {
        return new UpdateCheckResult(UpdateCheckStatus.InProgress, message);
    }

    public static UpdateCheckResult RestartRequested(string message)
    {
        return new UpdateCheckResult(UpdateCheckStatus.RestartRequested, message);
    }
}

public enum UpdateCheckStatus
{
    NoUpdate,
    Unavailable,
    InProgress,
    RestartRequested
}
