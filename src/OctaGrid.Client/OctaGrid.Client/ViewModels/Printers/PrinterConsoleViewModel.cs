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
/// The console of one printer: what it wrote, oldest first, kept up to date while the page is open. It only reads; there
/// is no way to send anything to the printer from here. Nothing is requested until <see cref="LoadCommand"/> runs, so a
/// printer page costs nothing until its Console tab is shown.
/// </summary>
public partial class PrinterConsoleViewModel : ViewModelBase
{
    /// <summary>How many lines one request asks for.</summary>
    internal const int PageSize = 200;

    /// <summary>How many lines the console keeps; older ones are dropped as new ones arrive.</summary>
    internal const int MaxShown = 500;

    // Bounds the catch-up after a long gap, so a flood cannot keep the loop running.
    private const int MaxPagesPerLoad = 10;

    private readonly IAgentCommandsService _commands;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ILogger<PrinterConsoleViewModel> _logger;
    private readonly Guid _printerId;
    private bool _closed;
    private long? _lastSequence;
    private bool _reloadRequested;
    private Guid? _streamId;

    public PrinterConsoleViewModel(
        Guid printerId,
        IAgentCommandsService commands,
        ILogger<PrinterConsoleViewModel>? logger = null)
    {
        _printerId = printerId;
        _commands = commands;
        _logger = logger ?? NullLogger<PrinterConsoleViewModel>.Instance;

        Lines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        WeakReferenceMessenger.Default.Register<PrinterConsoleData, Guid>(this, printerId, HandleRecorded);
    }

    /// <summary>Oldest first, so the newest line is at the bottom like in any console.</summary>
    public ObservableCollection<ConsoleLineItem> Lines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>True when the agent no longer has some older lines, so the console does not show everything.</summary>
    [ObservableProperty]
    public partial bool IsIncomplete { get; set; }

    /// <summary>True once a load has finished successfully, so "nothing yet" is never shown before the answer is in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    /// <summary>The agent answered and the printer has not written anything yet (or the view was cleared).</summary>
    public bool IsEmpty => IsLoaded && !IsLoading && !HasError && Lines.Count == 0;

    /// <summary>Stops listening and cancels what is running. Called when the page that shows the console is closed.</summary>
    internal void Stop()
    {
        _closed = true;
        _lifetime.Cancel();
        WeakReferenceMessenger.Default.Unregister<PrinterConsoleData, Guid>(this, _printerId);
    }

    /// <summary>
    /// Empties the list on screen. The agent's log is not touched, and what was cleared does not come back: the next lines
    /// are fetched after the last one that was shown.
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        Lines.Clear();
        IsIncomplete = false;
    }

    [RelayCommand]
    private Task Load(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    // Not the command itself: starting a cancelable command again cancels its run in progress, which is the opposite of
    // what a live line needs when it arrives during a load.
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
            _logger.LogDebug("Loading the console of printer {PrinterId} was cancelled", _printerId);
        }
        catch (AppException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the console of printer {PrinterId}", _printerId);
            ErrorMessage = "The console could not be loaded.";
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
            var page = await _commands.GetPrinterConsoleAsync(_printerId, _lastSequence, PageSize, cancellationToken);

            if (_streamId is { } known && known != page.StreamId)
            {
                // The agent started a new log (it restarted): what is shown, and the cursor taken from it, no longer
                // belong to it, even if its sequences have already passed ours.
                _logger.LogInformation("The console log of printer {PrinterId} started over; reloading", _printerId);
                Lines.Clear();
                _lastSequence = null;
                _streamId = null;
                IsIncomplete = false;
                continue;
            }

            _streamId = page.StreamId;
            if (page.Truncated)
            {
                IsIncomplete = true;
            }

            foreach (var line in page.Lines)
            {
                Add(new ConsoleLineItem(line));
            }

            if (_lastSequence is null || _lastSequence >= page.LatestSequence)
            {
                return;
            }
        }
    }

    private void Add(ConsoleLineItem item)
    {
        // A live line can arrive while a load is running, so the same line may be offered twice.
        if (item.Line.Sequence <= (_lastSequence ?? 0))
        {
            return;
        }

        Lines.Add(item);
        _lastSequence = item.Line.Sequence;

        while (Lines.Count > MaxShown)
        {
            Lines.RemoveAt(0);
            IsIncomplete = true;
        }
    }

    private void HandleRecorded(object recipient, PrinterConsoleData message)
    {
        if (_closed)
        {
            return;
        }

        if (IsLoading)
        {
            // The running load may have fetched before this line existed; it fetches once more when it is done.
            _reloadRequested = true;
            return;
        }

        if (!IsLoaded)
        {
            return;
        }

        var notice = message.Value;
        var sameStream = _streamId == notice.StreamId;
        if (sameStream && notice.Line.Sequence <= (_lastSequence ?? 0))
        {
            return; // already shown
        }

        if (sameStream && notice.Line.Sequence == (_lastSequence ?? 0) + 1)
        {
            Add(new ConsoleLineItem(notice.Line));
            return;
        }

        // Another log, or something in between was missed: ask the agent instead of guessing.
        _ = LoadAsync(_lifetime.Token);
    }
}
