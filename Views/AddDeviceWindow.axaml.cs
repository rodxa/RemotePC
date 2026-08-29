using Avalonia.Controls;
using RemotePC.Models;
using RemotePC.ViewModels;

namespace RemotePC.Views;

public partial class AddDeviceWindow : Window
{
    public AddDeviceWindow()
        : this(null)
    {
    }

    public AddDeviceWindow(PcDevice? device)
    {
        InitializeComponent();

        var viewModel = device is null
            ? new AddDeviceWindowViewModel()
            : new AddDeviceWindowViewModel(device);
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
