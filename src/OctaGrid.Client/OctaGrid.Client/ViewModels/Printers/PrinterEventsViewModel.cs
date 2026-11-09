using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Exceptions;

namespace OctaGrid.Client.ViewModels;

/// <summary>
/// The history dialog of one printer: what the agent recorded, newest first, kept up to date while it is open.
/// Nothing is requested until <see cref="LoadCommand"/> runs, so a dashboard full of cards costs nothing.
/// </summary>
public partial class PrinterEventsViewModel : ViewModelBase, IDialogParticipant<bool>
{
    /// <summary>How many events one request asks for.</summary>
    internal const int PageSize = 100;

    /// <summary>How many events the dialog keeps; older ones are dropped as new ones arrive.</summary>
    internal const int MaxShown = 200;

    // Bounds the catch-up after a long gap, so a flood cannot keep the loop running.
    private const int MaxPagesPerLoad = 10;

    private readonly IAgentCommandsService _commands;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ILogger<PrinterEventsViewModel> _logger;
    private readonly Guid _printerId;
    private bool _closed;
    private Guid? _historyId;
    private long? _lastSequence;
    private bool _reloadRequested;

    public PrinterEventsViewModel(
        Guid printerId,
        string printerName,
        IAgentCommandsService commands,
        ILogger<PrinterEventsViewModel>? logger = null)
    {
        _printerId = printerId;
        PrinterName = printerName;
        _commands = commands;
        _logger = logger ?? NullLogger<PrinterEventsViewModel>.Instance;

        Events.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        WeakReferenceMessenger.Default.Register<PrinterEventData, Guid>(this, printerId, HandleRecorded);
    }

    public string PrinterName { get; }

    /// <summary>Newest first.</summary>
    public ObservableCollection<PrinterEventItem> Events { get; } = [];

    /// <summary>True when the agent no longer has some older events, so the list does not show everything.</summary>
    [ObservableProperty]
    public partial bool IsIncomplete { get; set; }

    /// <summary>True once a load has finished successfully, so "nothing yet" is never shown before the answer is in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>The agent answered and nothing has happened yet.</summary>
    public bool IsEmpty => IsLoaded && !IsLoading && !HasError && Events.Count == 0;

    [RelayCommand]
    private void Close()
    {
        Stop();
        this.ReturnResultFromOverlayDialog(true);
    }

    /// <summary>Stops listening and cancels what is running. Separate from <see cref="CloseCommand"/> so it needs no dialog host.</summary>
    internal void Stop()
    {
        _closed = true;
        _lifetime.Cancel();
        WeakReferenceMessenger.Default.Unregister<PrinterEventData, Guid>(this, _printerId);
    }

    [RelayCommand]
    private Task Load(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    // Not the command itself: starting a cancelable command again cancels its run in progress, which is the opposite of
    // what a live event needs when it arrives during a load.
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsLoading)
        {
            // A load is already running and may have missed what prompted this one.
            _reloadRequested = true;
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            do
            {
                _reloadRequested = false;
                await FetchAsync(cancellationToken);
            }
            while (_reloadRequested);

            IsLoaded = true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Loading the history of printer {PrinterId} was cancelled", _printerId);
        }
        catch (AppException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the history of printer {PrinterId}", _printerId);
            ErrorMessage = "The history could not be loaded.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task FetchAsync(CancellationToken cancellationToken)
    {
        for (var pages = 0; pages < MaxPagesPerLoad; pages++)
        {
            var page = await _commands.GetPrinterEventsAsync(_printerId, _lastSequence, PageSize, cancellationToken);

            if (_historyId is { } known && known != page.HistoryId)
            {
                // The agent started a new history (it restarted, or forgot the printer): what is shown, and the cursor
                // taken from it, no longer belong to it, even if its sequences have already passed ours.
                _logger.LogInformation("The event history of printer {PrinterId} started over; reloading", _printerId);
                Events.Clear();
                _lastSequence = null;
                _historyId = null;
                IsIncomplete = false;
                continue;
            }

            _historyId = page.HistoryId;
            if (page.Truncated)
            {
                IsIncomplete = true;
            }

            foreach (var recorded in page.Events)
            {
                Add(new PrinterEventItem(recorded));
            }

            if (_lastSequence is null || _lastSequence >= page.LatestSequence)
            {
                return;
            }
        }
    }

    private void Add(PrinterEventItem item)
    {
        // A live event can arrive while a load is running, so the same event may be offered twice.
        if (item.Event.Sequence <= (_lastSequence ?? 0))
        {
            return;
        }

        Events.Insert(0, item);
        _lastSequence = item.Event.Sequence;

        while (Events.Count > MaxShown)
        {
            Events.RemoveAt(Events.Count - 1);
            IsIncomplete = true;
        }
    }

    private void HandleRecorded(object recipient, PrinterEventData message)
    {
        if (_closed)
        {
            return;
        }

        if (IsLoading)
        {
            // The running load may have fetched before this event existed; it fetches once more when it is done.
            _reloadRequested = true;
            return;
        }

        if (!IsLoaded)
        {
            return;
        }

        var notice = message.Value;
        var sameHistory = _historyId == notice.HistoryId;
        if (sameHistory && notice.Event.Sequence <= (_lastSequence ?? 0))
        {
            return; // already shown
        }

        if (sameHistory && notice.Event.Sequence == (_lastSequence ?? 0) + 1)
        {
            Add(new PrinterEventItem(notice.Event));
            return;
        }

        // Another history, or something in between was missed: ask the agent instead of guessing.
        _ = LoadAsync(_lifetime.Token);
    }
}
