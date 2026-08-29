using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemotePC.Models;
using RemotePC.Services;

namespace RemotePC.ViewModels;

public partial class AddDeviceWindowViewModel : ObservableObject
{
    public AddDeviceWindowViewModel()
    {
    }

    public AddDeviceWindowViewModel(PcDevice device)
    {
        IsEditing = true;
        DeviceName = device.DeviceName;
        DisplayName = device.DisplayName ?? string.Empty;
        TailscaleIp = device.TailscaleIp ?? string.Empty;
        RustDeskId = device.RustDeskId ?? string.Empty;
        Enabled = device.Enabled;
    }

    public bool IsEditing { get; }

    public string WindowTitle => IsEditing ? "Edit PC" : "Add PC";

    public string HeaderText => IsEditing ? "Edit PC" : "Add PC";

    public string SaveButtonText => IsEditing ? "Save" : "Add";

    [ObservableProperty]
    private string deviceName = string.Empty;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string tailscaleIp = string.Empty;

    [ObservableProperty]
    private string rustDeskId = string.Empty;

    [ObservableProperty]
    private bool enabled = true;

    [ObservableProperty]
    private string? errorMessage;

    public event EventHandler<PcDeviceCreateRequest>? SaveRequested;

    public event EventHandler? CancelRequested;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;

        var normalizedDeviceName = DeviceName.Trim();
        var normalizedRustDeskId = RustDeskService.NormalizeRustDeskId(RustDeskId);
        if (string.IsNullOrWhiteSpace(normalizedDeviceName))
        {
            ErrorMessage = "Device name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TailscaleIp))
        {
            ErrorMessage = "Tailscale IP is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedRustDeskId))
        {
            ErrorMessage = "RustDesk ID is required.";
            return;
        }

        SaveRequested?.Invoke(
            this,
            new PcDeviceCreateRequest
            {
                DeviceName = normalizedDeviceName,
                DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim(),
                TailscaleIp = TailscaleIp.Trim(),
                RustDeskId = normalizedRustDeskId,
                Enabled = Enabled
            });
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
