using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Services;

/// <summary>
/// Forwards backend notifications to view models as messenger messages.
/// <para>
/// All messages are posted to the UI thread. Notifications arrive on backend threads (agent threads in Local mode,
/// network threads in Remote mode), and the messenger invokes recipients on the sending thread, while recipients update
/// bound properties and collections. Messages are posted even when the caller is on the UI thread, which preserves
/// delivery order.
/// </para>
/// </summary>
internal sealed class MessageHandler : IClientMessageHandler
{
    private readonly PrinterTelemetryArrivals? _arrivals;
    private readonly ITemperatureHistoryStore? _temperatureHistories;

    /// <param name="temperatureHistories">Where the temperatures of every printer are collected for its chart; none means they are not.</param>
    /// <param name="arrivals">Where telemetry arrivals are counted; none means they are not.</param>
    public MessageHandler(ITemperatureHistoryStore? temperatureHistories = null, PrinterTelemetryArrivals? arrivals = null)
    {
        _temperatureHistories = temperatureHistories;
        _arrivals = arrivals;
    }

    public void Handle(EntityChanged entity)
    {
        if (entity.EntityType == EntityTypes.Printer)
        {
            if (entity.Kind == ChangeKind.Deleted)
            {
                _temperatureHistories?.Remove(entity.EntityId);
            }

            if (entity.Kind == ChangeKind.Upserted)
            {
                Post(new PrinterEntityUpsert(entity), entity.EntityId);
            }
            else if (entity.Kind == ChangeKind.Deleted)
            {
                Post(new PrinterEntityDelete(entity), entity.EntityId);
            }
        }
        if (entity.EntityType == EntityTypes.Workspace)
        {
            Post(new WorkspaceEntityChanged(entity), entity.EntityId);
        }
    }

    public void Handle(PrinterEventRecorded recorded)
    {
        PostTo(new PrinterEventData(recorded), recorded.PrinterId);
    }

    public void Handle(PrinterConsoleLineRecorded recorded)
    {
        PostTo(new PrinterConsoleData(recorded), recorded.PrinterId);
    }

    public void Handle(PrinterTelemetry telemetryMessage, TelemetrySample? sample)
    {
        // Recorded here and not by whatever shows the printer: the dashboard and the printer page come and go, and the
        // chart has to hold what happened while they were not there. TemperatureHistory is thread-safe, so recording
        // happens on the calling thread.
        if (sample is not null)
        {
            _temperatureHistories?.GetOrCreate(telemetryMessage.PrinterId).Add(sample);
        }

        // Counted before it is posted, so a state the watcher is about to post is known to be older than this one.
        _arrivals?.Record(telemetryMessage.PrinterId);
        PostTo(new PrinterTelemetryData(telemetryMessage), telemetryMessage.PrinterId);
    }

    /// <summary>Posts the message to recipients registered for <paramref name="entityId"/> and to untokened recipients.</summary>
    private static void Post<TMessage>(TMessage message, Guid entityId) where TMessage : class
    {
        Dispatcher.UIThread.Post(() =>
        {
            WeakReferenceMessenger.Default.Send(message, entityId);
            WeakReferenceMessenger.Default.Send(message);
        });
    }

    /// <summary>Posts the message to recipients registered for <paramref name="printerId"/> only.</summary>
    private static void PostTo<TMessage>(TMessage message, Guid printerId) where TMessage : class
    {
        Dispatcher.UIThread.Post(() => WeakReferenceMessenger.Default.Send(message, printerId));
    }
}
