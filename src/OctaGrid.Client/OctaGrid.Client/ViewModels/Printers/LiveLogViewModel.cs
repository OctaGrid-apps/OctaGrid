using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OctaGrid.Protocol.Exceptions;

namespace OctaGrid.Client.ViewModels;

/// <summary>One answer of the agent to a request for a log: the entries and what is needed to carry on from them.</summary>
/// <param name="Entries">The entries, oldest first.</param>
/// <param name="LatestSequence">The newest sequence the agent has; more can be fetched while this is ahead of what was received.</param>
/// <param name="Truncated">True when the agent could not reach back as far as asked, so the log is incomplete.</param>
/// <param name="StreamId">Identifies the log the entries belong to; a different id means the agent started over.</param>
public readonly record struct LogSlice<TEntry>(IReadOnlyList<TEntry> Entries, long LatestSequence, bool Truncated, Guid StreamId);

/// <summary>
/// A bounded list of what the agent keeps for one printer (its events, its console), newest first, that is loaded in
/// pages and then kept up to date from live notices. Both logs work the same way: entries have a sequence that grows by
/// one, the agent says when it started a new log, and a live notice may arrive while a load is still running. This class
/// holds that bookkeeping once; a subclass says how to fetch a page, how an entry looks, and how it hears live notices.
/// <para>
/// Nothing is requested until <see cref="LoadCommand"/> runs, so a page that has the log on a tab costs nothing until
/// the tab is shown.
/// </para>
/// </summary>
/// <typeparam name="TEntry">What the agent sends (an event, a console line).</typeparam>
/// <typeparam name="TItem">What a row of the list shows.</typeparam>
public abstract partial class LiveLogViewModel<TEntry, TItem> : ViewModelBase
{
    // Bounds the catch-up after a long gap, so a flood cannot keep the loop running.
    private const int MaxPagesPerLoad = 10;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly ILogger _logger;
    private readonly int _maxShown;
    private readonly string _noun;
    private readonly int _pageSize;
    private bool _closed;
    private long? _lastSequence;
    private bool _reloadRequested;
    private Guid? _streamId;

    /// <param name="printerId">The printer the log belongs to.</param>
    /// <param name="noun">What the log is called in messages and the log file, such as "console" or "history".</param>
    /// <param name="pageSize">How many entries one request asks for.</param>
    /// <param name="maxShown">How many entries the list keeps; older ones are dropped as new ones arrive.</param>
    /// <param name="logger">Where unexpected failures are written.</param>
    protected LiveLogViewModel(Guid printerId, string noun, int pageSize, int maxShown, ILogger logger)
    {
        PrinterId = printerId;
        _noun = noun;
        _pageSize = pageSize;
        _maxShown = maxShown;
        _logger = logger;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Loads the log, or brings it up to date when it is loaded already.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>The agent answered and there is nothing in the log (yet, or after the view was cleared).</summary>
    public bool IsEmpty => IsLoaded && !IsLoading && !HasError && Items.Count == 0;

    /// <summary>Why the log could not be loaded; null when it could.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>True when the agent no longer has some older entries, so the list does not show everything.</summary>
    [ObservableProperty]
    public partial bool IsIncomplete { get; set; }

    /// <summary>True once a load has finished successfully, so "nothing yet" is never shown before the answer is in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    /// <summary>The rows, newest first. Subclasses show them under the name that fits what they are.</summary>
    protected ObservableCollection<TItem> Items { get; } = [];

    /// <summary>Ends when the list is closed; what a subclass runs for the list should end with it.</summary>
    protected CancellationToken LifetimeToken => _lifetime.Token;

    protected Guid PrinterId { get; }

    /// <summary>Stops listening and cancels what is running. Called when whatever shows the list is closed.</summary>
    internal void Stop()
    {
        _closed = true;
        _lifetime.Cancel();
        StopListening();
    }

    /// <summary>Asks the agent for a page: what came after <paramref name="afterSequence"/>, or the newest when it is null.</summary>
    protected abstract Task<LogSlice<TEntry>> FetchAsync(long? afterSequence, int limit, CancellationToken cancellationToken);

    protected abstract TItem CreateItem(TEntry entry);

    protected abstract long SequenceOf(TEntry entry);

    /// <summary>Stops hearing live notices. Called once, by <see cref="Stop"/>.</summary>
    protected abstract void StopListening();

    /// <summary>
    /// A live entry arrived. It is added when it is the next one; when it is not (a gap, or another log) the agent is asked
    /// instead of guessing, and nothing is added until the list was loaded for the first time.
    /// </summary>
    protected void HandleLive(Guid streamId, TEntry entry)
    {
        if (_closed)
        {
            return;
        }

        if (IsLoading)
        {
            // The running load may have fetched before this entry existed; it fetches once more when it is done.
            _reloadRequested = true;
            return;
        }

        if (!IsLoaded)
        {
            return;
        }

        var sequence = SequenceOf(entry);
        var sameStream = _streamId == streamId;
        if (sameStream && sequence <= (_lastSequence ?? 0))
        {
            return; // already shown
        }

        if (sameStream && sequence == (_lastSequence ?? 0) + 1)
        {
            Add(entry);
            return;
        }

        // Another log, or something in between was missed: ask the agent instead of guessing.
        _ = LoadAsync(_lifetime.Token);
    }

    /// <summary>Empties what is shown but keeps the cursor, so what was cleared does not come back.</summary>
    protected void ClearShown()
    {
        Items.Clear();
        IsIncomplete = false;
    }

    // Not a command that cancels itself: starting a cancelable command again cancels its run in progress, which is the
    // opposite of what a live entry needs when it arrives during a load.
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
                await FetchPagesAsync(cancellationToken);
            }
            while (_reloadRequested);

            IsLoaded = true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Loading the {Noun} of printer {PrinterId} was cancelled", _noun, PrinterId);
        }
        catch (AppException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the {Noun} of printer {PrinterId}", _noun, PrinterId);
            ErrorMessage = $"The {_noun} could not be loaded.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task FetchPagesAsync(CancellationToken cancellationToken)
    {
        for (var pages = 0; pages < MaxPagesPerLoad; pages++)
        {
            var page = await FetchAsync(_lastSequence, _pageSize, cancellationToken);

            if (_streamId is { } known && known != page.StreamId)
            {
                // The agent started a new log (it restarted, or forgot the printer): what is shown, and the cursor taken
                // from it, no longer belong to it, even if its sequences have already passed ours.
                _logger.LogInformation("The {Noun} of printer {PrinterId} started over; reloading", _noun, PrinterId);
                Items.Clear();
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

            foreach (var entry in page.Entries)
            {
                Add(entry);
            }

            if (_lastSequence is null || _lastSequence >= page.LatestSequence)
            {
                return;
            }
        }
    }

    private void Add(TEntry entry)
    {
        // A live entry can arrive while a load is running, so the same entry may be offered twice.
        var sequence = SequenceOf(entry);
        if (sequence <= (_lastSequence ?? 0))
        {
            return;
        }

        Items.Insert(0, CreateItem(entry));
        _lastSequence = sequence;

        while (Items.Count > _maxShown)
        {
            Items.RemoveAt(Items.Count - 1);
            IsIncomplete = true;
        }
    }
}
