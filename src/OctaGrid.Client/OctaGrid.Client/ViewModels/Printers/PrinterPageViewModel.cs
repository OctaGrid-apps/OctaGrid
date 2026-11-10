using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Exceptions;

namespace OctaGrid.Client.ViewModels;

/// <summary>The tabs of the printer page, in the order they are shown.</summary>
public enum PrinterPageTab
{
    Overview,
    Files,
    Events,

    /// <summary>Only shown for printers whose connector reports a console.</summary>
    Console,
}

/// <summary>
/// The page of one printer, opened from a dashboard card. It shows the card's live <see cref="PrinterStatusWidget" />
/// and reuses the card's commands, so the card and the page cannot disagree about what a button does. The file list and
/// the events load when their tab is shown, so opening the page costs no more than the card already did.
/// </summary>
public partial class PrinterPageViewModel : ViewModelBase
{
    private readonly IAppSettingsStore _appSettings;
    private readonly INotificationService? _notifications;
    private readonly Action _close;
    private bool _consoleRequested;
    private bool _eventsRequested;
    private bool _stopped;

    public PrinterPageViewModel(
        PrinterDashboardCardViewModel card,
        Action close,
        IAppSettingsStore appSettings,
        INotificationService? notifications = null)
    {
        Card = card;
        _close = close;
        _appSettings = appSettings;
        _notifications = notifications;
        Files = card.CreateFilesViewModel();
        PrinterEvents = card.CreateEventsViewModel();
        PrinterConsole = card.CreateConsoleViewModel();
        HasConsole = card.SupportsConsole;
        PrinterConsole.IsExpertMode = appSettings.ClientSettings.IsExpertMode;

        Item.PropertyChanged += OnItemPropertyChanged;
        Files.PropertyChanged += OnFilesPropertyChanged;
        appSettings.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>The card that owns the printer's telemetry and commands.</summary>
    public PrinterDashboardCardViewModel Card { get; }

    /// <summary>The files on the printer.</summary>
    public FilePickerViewModel Files { get; }

    /// <summary>The files as rows with their own Print and Delete buttons; they follow <see cref="Files" />.</summary>
    public ObservableCollection<PrinterFileRowViewModel> FileRows { get; } = [];

    /// <summary>
    /// Whether the Console tab is shown: the printer's connector has to report a console. Decided when the page opens.
    /// </summary>
    public bool HasConsole { get; }

    /// <summary>What the printer wrote to its console, newest last. Only read; nothing can be sent from here.</summary>
    public PrinterConsoleViewModel PrinterConsole { get; }

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
        _appSettings.SettingsChanged -= OnSettingsChanged;
        PrinterEvents.Stop();
        PrinterConsole.Stop();
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

            case PrinterPageTab.Console when HasConsole && !_consoleRequested && !_stopped:
                _consoleRequested = true;
                _ = PrinterConsole.LoadCommand.ExecuteAsync(null).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                break;
        }
    }

    [RelayCommand]
    private void GoBack() => _close();

    // The card's command and the file list log what goes wrong before they rethrow, so a failure is only reported here:
    // a row's button must show the user what happened, and must not let the error escape from the click.
    private async Task PrintFileAsync(PrintMetadataListItem file, CancellationToken cancellationToken)
    {
        try
        {
            await Card.StartFileCommand.ExecuteAsync(file.Metadata.FileName);

            // The job is what the user wants to see next.
            SelectedTabIndex = (int)PrinterPageTab.Overview;
        }
        catch (OperationCanceledException)
        {
            // Nothing to report: the user or the page ended it.
        }
        catch (AppException ex)
        {
            _notifications?.ShowError("The print could not be started", ex.Message);
        }
        catch (Exception)
        {
            _notifications?.ShowError("The print could not be started", "Something went wrong. Check the printer and try again.");
        }
    }

    private async Task DeleteFileAsync(PrintMetadataListItem file, CancellationToken cancellationToken)
    {
        try
        {
            await Files.DeleteItemAsync(file, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Nothing to report.
        }
        catch (AppException ex)
        {
            _notifications?.ShowError("The file could not be deleted", ex.Message);
        }
        catch (Exception)
        {
            _notifications?.ShowError("The file could not be deleted", "Something went wrong. Check the printer and try again.");
        }
    }

    // Expert mode can be turned off while the page is open (in Settings, or in another window); the command box follows.
    private void OnSettingsChanged(object? sender, ClientSettings settings) =>
        PrinterConsole.IsExpertMode = settings.IsExpertMode;

    // The rows follow the file list, which is replaced as a whole each time it is loaded.
    private void OnFilesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FilePickerViewModel.Files))
        {
            return;
        }

        FileRows.Clear();
        foreach (var file in Files.Files)
        {
            FileRows.Add(new PrinterFileRowViewModel(file, PrintFileAsync, DeleteFileAsync, () => Item.CanStartNewPrint));
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PrinterStatusWidget.CanStartNewPrint))
        {
            foreach (var row in FileRows)
            {
                row.PrintCommand.NotifyCanExecuteChanged();
            }
        }
    }
}
