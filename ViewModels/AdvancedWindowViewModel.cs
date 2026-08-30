using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Models;
using RemotePC.Services;

namespace RemotePC.ViewModels;

public partial class AdvancedWindowViewModel : ObservableObject
{
    private readonly SupabaseService _supabase;
    private readonly RemoteHostClient _remoteHostClient;
    private readonly CancellationToken _applicationCancellationToken;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string remoteControlPassword = string.Empty;

    [ObservableProperty]
    private string? resultText;

    public AdvancedWindowViewModel(
        PcDevice device,
        SupabaseService supabase,
        RemoteHostClient remoteHostClient,
        CancellationToken applicationCancellationToken)
    {
        Device = device;
        _supabase = supabase;
        _remoteHostClient = remoteHostClient;
        _applicationCancellationToken = applicationCancellationToken;
    }

    public PcDevice Device { get; }

    public string Title => $"{Device.FriendlyName} / Advanced";

    public string Endpoint => string.IsNullOrWhiteSpace(Device.TailscaleHost)
        ? "No Tailscale IP configured"
        : $"http://{Device.TailscaleHost}:{Device.RemotePort}";

    public ObservableCollection<ActionCategoryViewModel> Categories { get; } = [];

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnResultTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasResult));
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var actions = await _supabase.GetCommandsAsync(Device.Id, _applicationCancellationToken);
            Categories.Clear();
            foreach (var group in actions.GroupBy(action => action.DisplayCategory).OrderBy(group => group.Key))
            {
                Categories.Add(new ActionCategoryViewModel(group.Key, group.Select(action => new ActionItemViewModel(action))));
            }
        }
        catch (Exception ex) when (ex is SupabaseException or HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AuthorizeAsync()
    {
        if (string.IsNullOrWhiteSpace(RemoteControlPassword))
        {
            ErrorMessage = "Enter the Remote Control password configured on the host PC.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var authorization = await _remoteHostClient.AuthorizeAsync(Device, RemoteControlPassword, _applicationCancellationToken);
            ResultText = authorization.Message;
            if (authorization.Success)
            {
                RemoteControlPassword = string.Empty;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<ActionExecutionResult> ShutdownAsync(bool confirmed)
    {
        return await RunAsync(() => _remoteHostClient.ShutdownAsync(Device, confirmed, _applicationCancellationToken));
    }

    public async Task<ActionExecutionResult> RestartAsync(bool confirmed)
    {
        return await RunAsync(() => _remoteHostClient.RestartAsync(Device, confirmed, _applicationCancellationToken));
    }

    public async Task<ActionExecutionResult> LockAsync()
    {
        return await RunAsync(() => _remoteHostClient.LockAsync(Device, _applicationCancellationToken));
    }

    public async Task<ActionExecutionResult> ExecuteActionAsync(PcCommand action, bool confirmed)
    {
        return await RunAsync(() => _remoteHostClient.ExecuteActionAsync(Device, action.Id, confirmed, _applicationCancellationToken));
    }

    public async Task<long> SaveCommandAsync(PcCommandSaveRequest action)
    {
        return await _supabase.SaveCommandAsync(action, _applicationCancellationToken);
    }

    public async Task DeleteCommandAsync(long actionId)
    {
        await _supabase.DeleteCommandAsync(actionId, _applicationCancellationToken);
    }

    public void ShowResult(ActionExecutionResult result)
    {
        ResultText =
            $"Success: {result.Success}{Environment.NewLine}" +
            $"Exit code: {(result.ExitCode?.ToString() ?? "n/a")}{Environment.NewLine}" +
            $"Duration: {result.DurationMs} ms{Environment.NewLine}" +
            $"{result.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"stdout{Environment.NewLine}{result.Stdout}{Environment.NewLine}" +
            $"stderr{Environment.NewLine}{result.Stderr}";
    }

    private async Task<ActionExecutionResult> RunAsync(Func<Task<ActionExecutionResult>> operation)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var result = await operation();
            ShowResult(result);
            return result;
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public sealed class ActionCategoryViewModel
{
    public ActionCategoryViewModel(string name, IEnumerable<ActionItemViewModel> actions)
    {
        Name = name;
        Actions = new ObservableCollection<ActionItemViewModel>(actions);
    }

    public string Name { get; }

    public ObservableCollection<ActionItemViewModel> Actions { get; }
}

public sealed class ActionItemViewModel
{
    public ActionItemViewModel(PcCommand action)
    {
        Action = action;
    }

    public PcCommand Action { get; }

    public string Name => Action.Name;

    public string Description => string.IsNullOrWhiteSpace(Action.Description) ? Action.CommandType : Action.Description;
}
