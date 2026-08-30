using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Models;
using RemotePC.Services;

namespace RemotePC.ViewModels;

public partial class ActionEditorWindowViewModel : ObservableObject
{
    private readonly AdvancedWindowViewModel _advanced;
    private readonly ActionSafetyService _safety = new();
    private long? _savedId;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private string category = "Custom";

    [ObservableProperty]
    private string commandType = PcCommandTypes.PowerShell;

    [ObservableProperty]
    private string command = string.Empty;

    [ObservableProperty]
    private string arguments = string.Empty;

    [ObservableProperty]
    private string workingDirectory = string.Empty;

    [ObservableProperty]
    private bool requireConfirmation = true;

    [ObservableProperty]
    private string timeoutSeconds = "30";

    [ObservableProperty]
    private bool enabled = true;

    [ObservableProperty]
    private string sortOrder = "0";

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? resultText;

    [ObservableProperty]
    private bool isBusy;

    public ActionEditorWindowViewModel(PcDevice device, PcCommand? action, AdvancedWindowViewModel advanced)
    {
        Device = device;
        _advanced = advanced;
        if (action is not null)
        {
            _savedId = action.Id;
            Name = action.Name;
            Description = action.Description ?? string.Empty;
            Category = action.Category ?? "Custom";
            CommandType = action.CommandType;
            Command = action.Command ?? string.Empty;
            Arguments = action.Arguments ?? string.Empty;
            WorkingDirectory = action.WorkingDirectory ?? string.Empty;
            RequireConfirmation = action.RequireConfirmation;
            TimeoutSeconds = action.TimeoutSeconds.ToString();
            Enabled = action.Enabled;
            SortOrder = action.SortOrder.ToString();
        }
    }

    public event EventHandler<bool>? CloseRequested;

    public PcDevice Device { get; }

    public string WindowTitle => _savedId is null ? "Add Action" : "Edit Action";

    public string[] CommandTypes { get; } = [PcCommandTypes.PowerShell, PcCommandTypes.Process];

    public bool IsPowerShell => CommandType == PcCommandTypes.PowerShell;

    public bool IsProcess => CommandType == PcCommandTypes.Process;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);

    public bool CanDelete => _savedId is not null;

    partial void OnCommandTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsPowerShell));
        OnPropertyChanged(nameof(IsProcess));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnResultTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasResult));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var request = Validate();
        if (request is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _savedId = await _advanced.SaveCommandAsync(request);
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        var request = Validate();
        if (request is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _savedId = await _advanced.SaveCommandAsync(request);
            var action = new PcCommand
            {
                Id = _savedId.Value,
                PcId = request.PcId,
                Name = request.Name,
                Description = request.Description,
                Category = request.Category,
                CommandType = request.CommandType,
                Command = request.Command,
                Arguments = request.Arguments,
                WorkingDirectory = request.WorkingDirectory,
                RequireConfirmation = request.RequireConfirmation,
                TimeoutSeconds = request.TimeoutSeconds,
                Enabled = request.Enabled,
                SortOrder = request.SortOrder
            };

            var result = await _advanced.ExecuteActionAsync(action, confirmed: true);
            ResultText =
                $"Success: {result.Success}{Environment.NewLine}" +
                $"Exit code: {(result.ExitCode?.ToString() ?? "n/a")}{Environment.NewLine}" +
                $"Duration: {result.DurationMs} ms{Environment.NewLine}" +
                $"{result.Message}{Environment.NewLine}{Environment.NewLine}" +
                $"stdout{Environment.NewLine}{result.Stdout}{Environment.NewLine}" +
                $"stderr{Environment.NewLine}{result.Stderr}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteAsync()
    {
        if (_savedId is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _advanced.DeleteCommandAsync(_savedId.Value);
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, false);
    }

    private PcCommandSaveRequest? Validate()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Name is required.";
            return null;
        }

        if (CommandType is not (PcCommandTypes.PowerShell or PcCommandTypes.Process))
        {
            ErrorMessage = "Choose a supported action type.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(Command))
        {
            ErrorMessage = CommandType == PcCommandTypes.PowerShell
                ? "PowerShell command is required."
                : "Executable path is required.";
            return null;
        }

        if (!int.TryParse(TimeoutSeconds, out var timeout) || timeout is < 1 or > 3600)
        {
            ErrorMessage = "Timeout must be between 1 and 3600 seconds.";
            return null;
        }

        if (!int.TryParse(SortOrder, out var sortOrder))
        {
            ErrorMessage = "Sort order must be a number.";
            return null;
        }

        var request = new PcCommandSaveRequest
        {
            Id = _savedId,
            PcId = Device.Id,
            Name = Name.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            Category = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim(),
            CommandType = CommandType,
            Command = Command.Trim(),
            Arguments = string.IsNullOrWhiteSpace(Arguments) ? null : Arguments.Trim(),
            WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory.Trim(),
            RequireConfirmation = RequireConfirmation,
            TimeoutSeconds = timeout,
            Enabled = Enabled,
            SortOrder = sortOrder
        };

        var safety = _safety.Validate(request);
        if (!safety.IsAllowed)
        {
            ErrorMessage = safety.Reason;
            return null;
        }

        return request;
    }
}
