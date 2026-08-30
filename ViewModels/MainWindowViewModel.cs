using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Models;
using RemotePC.Services;

namespace RemotePC.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly SupabaseService _supabaseService;
    private readonly PcStatusService _statusService;
    private readonly RustDeskService _rustDeskService;
    private readonly RemoteHostClient _remoteHostClient;
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
        RemoteHostClient remoteHostClient,
        CancellationToken applicationCancellationToken)
    {
        _supabaseService = supabaseService;
        _statusService = statusService;
        _rustDeskService = rustDeskService;
        _remoteHostClient = remoteHostClient;
        _applicationCancellationToken = applicationCancellationToken;
    }

    public ObservableCollection<PcViewModel> Pcs { get; } = [];

    public SupabaseService SupabaseService => _supabaseService;

    public RemoteHostClient RemoteHostClient => _remoteHostClient;

    public CancellationToken ApplicationCancellationToken => _applicationCancellationToken;

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

    public async Task AddDeviceAsync(PcDeviceCreateRequest request)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        OnPropertyChanged(nameof(ShowEmptyState));

        try
        {
            var pcId = await _supabaseService.AddPcAsync(request, _applicationCancellationToken);
            var existingPc = Pcs.FirstOrDefault(pc => pc.Device.Id == pcId);
            if (existingPc is not null)
            {
                await existingPc.RefreshStatusAsync(_applicationCancellationToken);
                return;
            }

            var device = new PcDevice
            {
                Id = pcId,
                DeviceName = request.DeviceName,
                DisplayName = request.DisplayName,
                CommandId = 0,
                TailscaleIp = request.TailscaleIp,
                RustDeskId = request.RustDeskId,
                Enabled = request.Enabled,
                SortOrder = Pcs.Count == 0 ? 0 : Pcs.Max(pc => pc.Device.SortOrder) + 10,
                UpdatedAt = DateTimeOffset.UtcNow,
                RemotePort = request.RemotePort,
                RemoteEnabled = request.RemoteEnabled,
                RemoteDeviceId = request.RemoteDeviceId
            };

            var pc = new PcViewModel(
                device,
                _supabaseService,
                _statusService,
                _rustDeskService,
                _remoteHostClient,
                _applicationCancellationToken);

            Pcs.Add(pc);
            OnPropertyChanged(nameof(HasPcs));
            OnPropertyChanged(nameof(ShowEmptyState));
            await pc.RefreshStatusAsync(_applicationCancellationToken);
        }
        catch (Exception ex) when (ex is SupabaseException or HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = ex.Message;
            return;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    public async Task UpdateDeviceAsync(PcViewModel pc, PcDeviceCreateRequest request)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        OnPropertyChanged(nameof(ShowEmptyState));

        try
        {
            await _supabaseService.UpdatePcAsync(pc.Device.Id, request, _applicationCancellationToken);
        }
        catch (Exception ex) when (ex is SupabaseException or HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = ex.Message;
            return;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmptyState));
        }

        await RefreshAsync();
    }

    public async Task DeleteDeviceAsync(PcViewModel pc)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        OnPropertyChanged(nameof(ShowEmptyState));

        try
        {
            await _supabaseService.DeletePcAsync(pc.Device.Id, _applicationCancellationToken);
            Pcs.Remove(pc);
            OnPropertyChanged(nameof(HasPcs));
            OnPropertyChanged(nameof(ShowEmptyState));
        }
        catch (Exception ex) when (ex is SupabaseException or HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmptyState));
        }
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
            var devices = await _supabaseService.GetPcsAsync(cancellationToken);
            Pcs.Clear();

            foreach (var device in devices)
            {
                Pcs.Add(new PcViewModel(
                    device,
                    _supabaseService,
                    _statusService,
                    _rustDeskService,
                    _remoteHostClient,
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
            ErrorMessage?.Contains("add_pc_device", StringComparison.OrdinalIgnoreCase) == true ||
            ErrorMessage?.Contains("update_pc_device", StringComparison.OrdinalIgnoreCase) == true ||
            ErrorMessage?.Contains("delete_pc_device", StringComparison.OrdinalIgnoreCase) == true ||
            ErrorMessage?.Contains("PGRST202", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Steps: run the SQL migration, confirm the RPC exists, reload schema.";
        }

        if (!string.IsNullOrWhiteSpace(ErrorMessage))
        {
            return "Steps: check internet, Supabase project, table permissions, then refresh.";
        }

        return string.Empty;
    }
}
