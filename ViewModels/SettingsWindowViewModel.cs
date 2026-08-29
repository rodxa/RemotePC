using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Configuration;

namespace RemotePC.ViewModels;

public partial class SettingsWindowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string supabaseUrl = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string publishableKey = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    public SettingsWindowViewModel()
    {
        var options = AppConfiguration.LoadForEditing();
        SupabaseUrl = options.Url;
        PublishableKey = options.PublishableKey;
    }

    public event EventHandler<bool>? CloseRequested;

    public string SettingsPath => AppConfiguration.GetSettingsPath();

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var options = new SupabaseOptions
        {
            Url = SupabaseUrl,
            PublishableKey = PublishableKey
        }.Validated();

        if (!options.IsConfigured)
        {
            ErrorMessage = options.ConfigurationError;
            return;
        }

        try
        {
            AppConfiguration.Save(options);
            ErrorMessage = null;
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"Could not save settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, false);
    }

    private bool CanSave()
    {
        return !string.IsNullOrWhiteSpace(SupabaseUrl) &&
               !string.IsNullOrWhiteSpace(PublishableKey);
    }
}
