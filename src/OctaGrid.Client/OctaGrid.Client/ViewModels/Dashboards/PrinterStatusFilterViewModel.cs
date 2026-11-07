using CommunityToolkit.Mvvm.ComponentModel;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.ViewModels;

/// <summary>A status badge above the list dashboard: the printers in one state, selectable as a filter.</summary>
public partial class PrinterStatusFilterViewModel : ViewModelBase
{
    public PrinterStatusFilterViewModel(UniversalPrinterState state)
    {
        State = state;
    }

    public UniversalPrinterState State { get; }

    /// <summary>How many printers are in <see cref="State" /> (within the selected connector).</summary>
    [ObservableProperty]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
