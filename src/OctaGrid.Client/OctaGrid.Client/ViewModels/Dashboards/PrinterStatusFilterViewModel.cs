using CommunityToolkit.Mvvm.ComponentModel;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.ViewModels;

/// <summary>A status badge above the list dashboard: the printers in one state, selectable as a filter.</summary>
public partial class PrinterStatusFilterViewModel : ViewModelBase
{
    public PrinterStatusFilterViewModel(UniversalPrinterState state)
    {
        State = state;
    }

    /// <summary>How many printers have reported <see cref="State" /> (within the selected connector).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    public partial int Count { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    public partial bool IsSelected { get; set; }

    /// <summary>Empty states are hidden, except a selected one so the filter can still be cleared.</summary>
    public bool IsVisible => Count > 0 || IsSelected;

    public UniversalPrinterState State { get; }
}
