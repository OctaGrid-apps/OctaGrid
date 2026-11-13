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

    public UniversalPrinterState State { get; }

    /// <summary>How many printers are in <see cref="State" /> (within the selected connector).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    public partial int Count { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// True once every printer has reported a state, so an empty badge means "no printers" and not "not reported yet".
    /// Until then all badges stay visible, which keeps the row from filling in and shifting the cards at startup.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    public partial bool HideWhenEmpty { get; set; }

    /// <summary>Empty states are hidden once states are known, except a selected one so the filter can still be cleared.</summary>
    public bool IsVisible => !HideWhenEmpty || Count > 0 || IsSelected;
}
