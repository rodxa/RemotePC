using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Services;

namespace RemotePC.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly SupabaseService _supabaseService;
    private readonly PcStatusService _statusService;
    private readonly RustDeskService _rustDeskService;
    private readonly CancellationToken _applicationCancellationToken;
    private CancellationTokenSource? _refreshCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    public MainWindowViewModel(
        SupabaseService supabaseService,
        PcStatusService statusService,
        RustDeskService rustDeskService,
        CancellationToken applicationCancellationToken)
    {
        _supabaseService = supabaseService;
        _statusService = statusService;
        _rustDeskService = rustDeskService;
        _applicationCancellationToken = applicationCancellationToken;
    }

    public ObservableCollection<PcViewModel> Pcs { get; } = [];

    public bool HasPcs => Pcs.Count > 0;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool ShowSettingsPrompt =>
        HasError &&
        (ErrorMessage!.Contains("appsettings", StringComparison.OrdinalIgnoreCase) ||
         ErrorMessage.Contains("Supabase:", StringComparison.OrdinalIgnoreCase));

    public bool HasTroubleshooting => !string.IsNullOrWhiteSpace(TroubleshootingText);

    public string TroubleshootingText => GetTroubleshootingText();

    public bool ShowEmptyState => !IsLoading && ErrorMessage is null && Pcs.Count == 0;

    public async Task InitializeAsync()
    {
        await RefreshAsync();
    }

    public async Task ReloadConfigurationAndRefreshAsync()
    {
        _supabaseService.ReloadConfiguration();
        await RefreshAsync();
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowSettingsPrompt));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(TroubleshootingText));
        OnPropertyChanged(nameof(HasTroubleshooting));
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_applicationCancellationToken);
        var cancellationToken = _refreshCts.Token;

        IsLoading = true;
        ErrorMessage = null;
        OnPropertyChanged(nameof(ShowEmptyState));

        try
        {
            var devices = await _supabaseService.GetEnabledPcsAsync(cancellationToken);
            Pcs.Clear();

            foreach (var device in devices)
            {
                Pcs.Add(new PcViewModel(
                    device,
                    _supabaseService,
                    _statusService,
                    _rustDeskService,
                    _applicationCancellationToken));
            }

            OnPropertyChanged(nameof(HasPcs));
            OnPropertyChanged(nameof(ShowEmptyState));

            var checks = Pcs.Select(pc => pc.RefreshStatusAsync(cancellationToken));
            await Task.WhenAll(checks);
        }
        catch (OperationCanceledException) when (_applicationCancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Refresh cancelled.";
        }
        catch (Exception ex) when (ex is SupabaseException or HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = ex.Message;
            Pcs.Clear();
            OnPropertyChanged(nameof(HasPcs));
            OnPropertyChanged(nameof(ShowEmptyState));
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    private bool CanRefresh()
    {
        return !IsLoading;
    }

    private string GetTroubleshootingText()
    {
        if (ErrorMessage?.Contains("appsettings", StringComparison.OrdinalIgnoreCase) == true ||
            ErrorMessage?.Contains("Supabase:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Steps: open Settings, add Supabase URL and publishable key, save, refresh.";
        }

        if (ErrorMessage?.Contains("wake_pc", StringComparison.OrdinalIgnoreCase) == true ||
            ErrorMessage?.Contains("PGRST202", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Steps: run the SQL migration, confirm wake_pc exists, reload schema.";
        }

        if (!string.IsNullOrWhiteSpace(ErrorMessage))
        {
            return "Steps: check internet, Supabase project, table permissions, then refresh.";
        }

        return string.Empty;
    }
}
