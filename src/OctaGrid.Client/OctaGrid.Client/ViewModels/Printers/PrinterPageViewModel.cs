using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OctaGrid.Client.ViewModels;

/// <summary>The tabs of the printer page, in the order they are shown.</summary>
public enum PrinterPageTab
{
    Overview,
    Files,
    Events,
}

/// <summary>
/// The page of one printer, opened from a dashboard card. It shows the card's live <see cref="PrinterStatusWidget" />
/// and reuses the card's commands, so the card and the page cannot disagree about what a button does. The file list and
/// the events load when their tab is shown, so opening the page costs no more than the card already did.
/// </summary>
public partial class PrinterPageViewModel : ViewModelBase
{
    private readonly Action _close;
    private bool _eventsRequested;
    private bool _stopped;

    public PrinterPageViewModel(PrinterDashboardCardViewModel card, Action close)
    {
        Card = card;
        _close = close;
        Files = card.CreateFilesViewModel();
        PrinterEvents = card.CreateEventsViewModel();

        Item.PropertyChanged += OnItemPropertyChanged;
        Files.PropertyChanged += OnFilesPropertyChanged;
    }

    /// <summary>The card that owns the printer's telemetry and commands.</summary>
    public PrinterDashboardCardViewModel Card { get; }

    /// <summary>The files on the printer.</summary>
    public FilePickerViewModel Files { get; }

    /// <summary>What happened to the printer (state changes, warnings), newest first.</summary>
    public PrinterEventsViewModel PrinterEvents { get; }

    /// <summary>The name of the connector the printer uses, shown under the printer's name.</summary>
    public string ConnectorName => Card.ConnectorName;

    /// <summary>The live status of the printer.</summary>
    public PrinterStatusWidget Item => Card.Item;

    /// <summary>The index of the shown tab, as a <see cref="PrinterPageTab" />.</summary>
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    /// <summary>Stops listening for the printer's events. Called when the page is closed, however it was closed.</summary>
    internal void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        Item.PropertyChanged -= OnItemPropertyChanged;
        Files.PropertyChanged -= OnFilesPropertyChanged;
        PrinterEvents.Stop();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        switch ((PrinterPageTab)value)
        {
            case PrinterPageTab.Files:
                // The files can change on the printer at any time, so they are asked for each time the tab is shown.
                _ = Files.LoadDataCommand.ExecuteAsync(null).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                break;

            case PrinterPageTab.Events when !_eventsRequested && !_stopped:
                // Once is enough: the list keeps itself up to date from the live events.
                _eventsRequested = true;
                _ = PrinterEvents.LoadCommand.ExecuteAsync(null).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                break;
        }
    }

    private bool CanPrintSelectedFile() => Files.SelectedItem is not null && Item.CanStartNewPrint;

    [RelayCommand]
    private void GoBack() => _close();

    [RelayCommand(CanExecute = nameof(CanPrintSelectedFile))]
    private async Task PrintSelectedFile(CancellationToken cancellationToken)
    {
        if (Files.SelectedItem is not { } selected)
        {
            return;
        }

        await Card.StartFileCommand.ExecuteAsync(selected.Metadata.FileName);

        // The job is what the user wants to see next.
        SelectedTabIndex = (int)PrinterPageTab.Overview;
    }

    private void OnFilesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilePickerViewModel.SelectedItem))
        {
            PrintSelectedFileCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PrinterStatusWidget.CanStartNewPrint))
        {
            PrintSelectedFileCommand.NotifyCanExecuteChanged();
        }
    }
}
