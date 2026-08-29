using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Services;

namespace RemotePC.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly SupabaseService _supabaseService;
    private readonly PcStatusService _statusService;
    private readonly ParsecService _parsecService;
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
        ParsecService parsecService,
        CancellationToken applicationCancellationToken)
    {
        _supabaseService = supabaseService;
        _statusService = statusService;
        _parsecService = parsecService;
        _applicationCancellationToken = applicationCancellationToken;
    }

    public ObservableCollection<PcViewModel> Pcs { get; } = [];

    public bool HasPcs => Pcs.Count > 0;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool ShowSettingsPrompt =>
        HasError &&
        (ErrorMessage!.Contains("appsettings", StringComparison.OrdinalIgnoreCase) ||
         ErrorMessage.Contains("Supabase:", StringComparison.OrdinalIgnoreCase));

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
                    _parsecService,
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
}
