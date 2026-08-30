using Avalonia.Controls;
using RemotePC.Models;
using RemotePC.Services;
using RemotePC.ViewModels;

namespace RemotePC.Views;

public partial class AddDeviceWindow : Window
{
    public AddDeviceWindow()
        : this((PcDevice?)null, null)
    {
    }

    public AddDeviceWindow(RemoteHostClient remoteHostClient)
        : this(null, remoteHostClient)
    {
    }

    public AddDeviceWindow(PcDevice? device)
        : this(device, null)
    {
    }

    public AddDeviceWindow(PcDevice? device, RemoteHostClient? remoteHostClient)
    {
        InitializeComponent();

        var viewModel = device is null
            ? remoteHostClient is null
                ? new AddDeviceWindowViewModel()
                : new AddDeviceWindowViewModel(remoteHostClient)
            : new AddDeviceWindowViewModel(device, remoteHostClient);
        viewModel.SaveRequested += OnSaveRequested;
        viewModel.CancelRequested += OnCancelRequested;
        DataContext = viewModel;
    }

    private void OnSaveRequested(object? sender, PcDeviceCreateRequest request)
    {
        Unsubscribe();
        Close(request);
    }

    private void OnCancelRequested(object? sender, EventArgs e)
    {
        Unsubscribe();
        Close(null);
    }

    private void Unsubscribe()
    {
        if (DataContext is AddDeviceWindowViewModel viewModel)
        {
            viewModel.SaveRequested -= OnSaveRequested;
            viewModel.CancelRequested -= OnCancelRequested;
        }
    }
}
