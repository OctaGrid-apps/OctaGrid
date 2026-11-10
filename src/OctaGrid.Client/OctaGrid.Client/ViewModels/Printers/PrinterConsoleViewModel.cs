using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Exceptions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.ViewModels;

/// <summary>
/// The console of one printer: what it wrote, newest first, kept up to date while the page is open. Nothing is requested
/// until <see cref="LoadCommand"/> runs, so a printer page costs nothing until its Console tab is shown.
/// <para>
/// Reading is open to everyone. Sending a command can change or stop what the printer is doing, so the command box exists
/// only when the printer's connector can take commands (<see cref="SupportsCommands"/>) and the user turned on Expert mode
/// (<see cref="IsExpertMode"/>); together they make <see cref="CanSend"/>.
/// </para>
/// </summary>
public partial class PrinterConsoleViewModel : ViewModelBase, IDialogParticipant
{
    /// <summary>How many lines one request asks for.</summary>
    internal const int PageSize = 200;

    /// <summary>How many lines the console keeps; older ones are dropped as new ones arrive.</summary>
    internal const int MaxShown = 500;

    /// <summary>How many sent commands are remembered for recalling with the arrow keys.</summary>
    internal const int MaxRemembered = 50;

    // Bounds the catch-up after a long gap, so a flood cannot keep the loop running.
    private const int MaxPagesPerLoad = 10;

    // Commands that can interrupt a print or reset the printer's settings are confirmed one by one. Emergency stops (M112)
    // are deliberately not on the list: they have to work at once.
    private static readonly HashSet<string> RiskyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "FIRMWARE_RESTART", "RESTART", "SAVE_CONFIG", "M502", "M997",
    };

    private readonly IAgentCommandsService _commands;
    private readonly List<string> _history = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ILogger<PrinterConsoleViewModel> _logger;
    private readonly Guid _printerId;
    private bool _closed;
    private int _historyIndex;
    private long? _lastSequence;
    private bool _reloadRequested;
    private Guid? _streamId;

    public PrinterConsoleViewModel(
        Guid printerId,
        IAgentCommandsService commands,
        ILogger<PrinterConsoleViewModel>? logger = null,
        bool supportsCommands = false)
    {
        _printerId = printerId;
        _commands = commands;
        _logger = logger ?? NullLogger<PrinterConsoleViewModel>.Instance;
        SupportsCommands = supportsCommands;

        Lines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        WeakReferenceMessenger.Default.Register<PrinterConsoleData, Guid>(this, printerId, HandleRecorded);
    }

    /// <summary>Newest first, so what the printer just wrote is at the top and nobody has to scroll for it.</summary>
    public ObservableCollection<ConsoleLineItem> Lines { get; } = [];

    /// <summary>True when the printer's connector can take commands. Decided when the page opens.</summary>
    public bool SupportsCommands { get; }

    /// <summary>
    /// Whether the user turned Expert mode on. Set by the page that shows the console, which follows the setting, so a
    /// console that is open stops offering the command box the moment Expert mode is turned off.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(ShowExpertModeHint))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsExpertMode { get; set; }

    /// <summary>The command box is offered: the printer can take commands and Expert mode is on.</summary>
    public bool CanSend => SupportsCommands && IsExpertMode;

    /// <summary>The printer can take commands but Expert mode is off, so the console shows a quiet icon that says how to get the command box.</summary>
    public bool ShowExpertModeHint => SupportsCommands && !IsExpertMode;

    /// <summary>What is typed in the command box.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string CommandText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; set; }

    /// <summary>Why the last command was not sent or was refused by the printer; null when it went well.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSendError))]
    public partial string? SendError { get; set; }

    public bool HasSendError => !string.IsNullOrEmpty(SendError);

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

    /// <summary>
    /// The lines on screen as text to copy: one per line with its time, oldest first like a log file, although the list
    /// shows the newest first. Empty when there is nothing to copy.
    /// </summary>
    public string GetTranscript() =>
        string.Join(Environment.NewLine, Lines.Reverse().Select(l => $"{l.TimeText} {l.Text}"));

    /// <summary>Whether a command is risky enough to be confirmed before it is sent, and its name for the question.</summary>
    internal static bool IsRisky(string command, out string name)
    {
        name = command.Split(' ', 2)[0].ToUpperInvariant();
        return RiskyCommands.Contains(name);
    }

    private bool CanSendCommand() => CanSend && !IsSending && !string.IsNullOrWhiteSpace(CommandText);

    [RelayCommand]
    private Task Load(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    /// <summary>Brings back the command sent before the one in the box, like the up arrow of a terminal.</summary>
    [RelayCommand]
    private void RecallPrevious()
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Max(0, _historyIndex - 1);
        CommandText = _history[_historyIndex];
    }

    /// <summary>Moves forward again through the sent commands; past the newest one the box is empty.</summary>
    [RelayCommand]
    private void RecallNext()
    {
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            CommandText = _history[_historyIndex];
        }
        else
        {
            _historyIndex = _history.Count;
            CommandText = string.Empty;
        }
    }

    /// <summary>
    /// Sends the command in the box. The rules are checked here and again by the agent; a risky command is confirmed first.
    /// What the printer answers arrives as console lines, so nothing is shown for it here.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSendCommand))]
    private async Task Send(CancellationToken cancellationToken)
    {
        if (!ConsoleCommandRules.TryNormalize(CommandText, out var command, out var error))
        {
            SendError = error;
            return;
        }

        if (IsRisky(command, out var name))
        {
            var confirm = new ConfirmViewModel(
                $"{name} can interrupt a print or reset the printer's settings. Send it to the printer?", "Send");
            if (await this.ShowOverlayDialogAsync("Send command", confirm) != true)
            {
                return;
            }
        }

        // Expert mode may have been turned off while the question was open.
        if (!CanSend)
        {
            return;
        }

        IsSending = true;
        SendError = null;
        try
        {
            await _commands.SendConsoleCommandAsync(_printerId, command, cancellationToken);
            Remember(command);
            CommandText = string.Empty;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Sending a command to printer {PrinterId} was cancelled", _printerId);
        }
        catch (AppException ex)
        {
            SendError = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send a command to printer {PrinterId}", _printerId);
            SendError = "The command could not be sent.";
        }
        finally
        {
            IsSending = false;
        }
    }

    private void Remember(string command)
    {
        if (_history.Count == 0 || _history[^1] != command)
        {
            _history.Add(command);
            if (_history.Count > MaxRemembered)
            {
                _history.RemoveAt(0);
            }
        }

        _historyIndex = _history.Count;
    }

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

        Lines.Insert(0, item);
        _lastSequence = item.Line.Sequence;

        while (Lines.Count > MaxShown)
        {
            Lines.RemoveAt(Lines.Count - 1);
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
