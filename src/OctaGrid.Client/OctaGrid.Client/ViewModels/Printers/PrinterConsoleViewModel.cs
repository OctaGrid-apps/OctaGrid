using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.ViewModels;

/// <summary>
/// The console of one printer: what it wrote, newest first, kept up to date while the page is open. Loading, paging and
/// live notices are in <see cref="LiveLogViewModel{TEntry, TItem}" />; nothing is requested until
/// <see cref="LiveLogViewModel{TEntry, TItem}.LoadCommand" /> runs, so a printer page costs nothing until its Console tab
/// is shown.
/// <para>
/// Reading is open to everyone. Sending a command can change or stop what the printer is doing, so the command box exists
/// only when the printer's connector can take commands (<see cref="SupportsCommands"/>) and the user turned on Expert mode
/// (<see cref="IsExpertMode"/>); together they make <see cref="CanSend"/>.
/// </para>
/// </summary>
public partial class PrinterConsoleViewModel : LiveLogViewModel<ConsoleLine, ConsoleLineItem>
{
    /// <summary>How many lines one request asks for.</summary>
    internal const int PageSize = 200;

    /// <summary>How many lines the console keeps; older ones are dropped as new ones arrive.</summary>
    internal const int MaxShown = 500;

    /// <summary>How many sent commands are remembered for recalling with the arrow keys.</summary>
    internal const int MaxRemembered = 50;

    private readonly IAgentCommandsService _commands;
    private readonly List<string> _history = [];
    private readonly ILogger<PrinterConsoleViewModel> _logger;
    private int _historyIndex;

    public PrinterConsoleViewModel(
        Guid printerId,
        IAgentCommandsService commands,
        ILogger<PrinterConsoleViewModel>? logger = null,
        bool supportsCommands = false)
        : base(printerId, "console", PageSize, MaxShown, logger ?? NullLogger<PrinterConsoleViewModel>.Instance)
    {
        _commands = commands;
        _logger = logger ?? NullLogger<PrinterConsoleViewModel>.Instance;
        SupportsCommands = supportsCommands;

        WeakReferenceMessenger.Default.Register<PrinterConsoleData, Guid>(this, printerId, HandleRecorded);
    }

    /// <summary>Newest first, so what the printer just wrote is at the top and nobody has to scroll for it.</summary>
    public ObservableCollection<ConsoleLineItem> Lines => Items;

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

    /// <summary>Why the last command was not sent or was refused by the printer; null when it went well.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSendError))]
    public partial string? SendError { get; set; }

    public bool HasSendError => !string.IsNullOrEmpty(SendError);

    /// <summary>
    /// The lines on screen as text to copy: one per line with its time, oldest first like a log file, although the list
    /// shows the newest first. Empty when there is nothing to copy.
    /// </summary>
    public string GetTranscript() =>
        string.Join(Environment.NewLine, Lines.Reverse().Select(l => $"{l.TimeText} {l.Text}"));

    protected override ConsoleLineItem CreateItem(ConsoleLine entry) => new(entry);

    protected override async Task<LogSlice<ConsoleLine>> FetchAsync(long? afterSequence, int limit, CancellationToken cancellationToken)
    {
        var page = await _commands.GetPrinterConsoleAsync(PrinterId, afterSequence, limit, cancellationToken);
        return new LogSlice<ConsoleLine>(page.Lines, page.LatestSequence, page.Truncated, page.StreamId);
    }

    protected override long SequenceOf(ConsoleLine entry) => entry.Sequence;

    protected override void StopListening() =>
        WeakReferenceMessenger.Default.Unregister<PrinterConsoleData, Guid>(this, PrinterId);

    /// <summary>
    /// Empties the list on screen. The agent's log is not touched, and what was cleared does not come back: the next lines
    /// are fetched after the last one that was shown.
    /// </summary>
    [RelayCommand]
    private void Clear() => ClearShown();

    private bool CanSendCommand() => CanSend && !string.IsNullOrWhiteSpace(CommandText);

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
    /// Sends the command in the box. The rules are checked here and again by the agent. What the printer answers arrives
    /// as console lines, so nothing is shown for it here.
    /// <para>
    /// The command takes no cancellation token of its own on purpose: the toolkit cancels a command's earlier run when it
    /// runs again, which would cancel the wait for the long command the moment the emergency stop is sent. What ends a
    /// send is the console being closed (<see cref="LiveLogViewModel{TEntry, TItem}.Stop" />).
    /// </para>
    /// <para>
    /// Commands may overlap on purpose. A printer can be busy with one command for minutes (homing, heating up), and the
    /// box must stay free for the next one: an emergency stop cannot wait for the one before it. So the box is emptied as
    /// soon as the command is on its way, and is given back to the user only if the printer then refuses it.
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSendCommand), AllowConcurrentExecutions = true)]
    private async Task Send()
    {
        if (!ConsoleCommandRules.TryNormalize(CommandText, out var command, out var error))
        {
            SendError = error;
            return;
        }

        CommandText = string.Empty;
        SendError = null;
        try
        {
            await _commands.SendConsoleCommandAsync(PrinterId, command, LifetimeToken);

            // Only what the printer accepted is remembered for recall: a command it refused (a typo, a wrong argument) would
            // only come back to be refused again, and one with a secret in it has no reason to stay.
            Remember(command);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Sending a command to printer {PrinterId} was cancelled", PrinterId);
        }
        catch (AppException ex)
        {
            Refused(command, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send a command to printer {PrinterId}", PrinterId);
            Refused(command, "The command could not be sent.");
        }
    }

    // The user gets the command back to fix it, unless they have already started typing something else.
    private void Refused(string command, string reason)
    {
        SendError = reason;
        if (string.IsNullOrEmpty(CommandText))
        {
            CommandText = command;
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

    private void HandleRecorded(object recipient, PrinterConsoleData message) =>
        HandleLive(message.Value.StreamId, message.Value.Line);
}
