using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.ViewModels;

/// <summary>
/// The events of one printer: what the agent recorded, newest first, kept up to date while it is shown (in the history
/// dialog and on the printer page). Loading, paging and live notices are in <see cref="LiveLogViewModel{TEntry, TItem}" />.
/// </summary>
public partial class PrinterEventsViewModel : LiveLogViewModel<PrinterEvent, PrinterEventItem>, IDialogParticipant<bool>
{
    /// <summary>How many events one request asks for.</summary>
    internal const int PageSize = 100;

    /// <summary>How many events the list keeps; older ones are dropped as new ones arrive.</summary>
    internal const int MaxShown = 200;

    private readonly IAgentCommandsService _commands;

    public PrinterEventsViewModel(
        Guid printerId,
        string printerName,
        IAgentCommandsService commands,
        ILogger<PrinterEventsViewModel>? logger = null)
        : base(printerId, "history", PageSize, MaxShown, logger ?? NullLogger<PrinterEventsViewModel>.Instance)
    {
        PrinterName = printerName;
        _commands = commands;

        WeakReferenceMessenger.Default.Register<PrinterEventData, Guid>(this, printerId, HandleRecorded);
    }

    public string PrinterName { get; }

    /// <summary>Newest first.</summary>
    public ObservableCollection<PrinterEventItem> Events => Items;

    protected override PrinterEventItem CreateItem(PrinterEvent entry) => new(entry);

    protected override async Task<LogSlice<PrinterEvent>> FetchAsync(long? afterSequence, int limit, CancellationToken cancellationToken)
    {
        var page = await _commands.GetPrinterEventsAsync(PrinterId, afterSequence, limit, cancellationToken);
        return new LogSlice<PrinterEvent>(page.Events, page.LatestSequence, page.Truncated, page.HistoryId);
    }

    protected override long SequenceOf(PrinterEvent entry) => entry.Sequence;

    protected override void StopListening() =>
        WeakReferenceMessenger.Default.Unregister<PrinterEventData, Guid>(this, PrinterId);

    [RelayCommand]
    private void Close()
    {
        Stop();
        this.ReturnResultFromOverlayDialog(true);
    }

    private void HandleRecorded(object recipient, PrinterEventData message) =>
        HandleLive(message.Value.HistoryId, message.Value.Event);
}
