using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Services;

internal sealed class MessageHandler : IClientMessageHandler
{
    public void Handle(EntityChanged entity)
    {
        if (entity.EntityType == EntityTypes.Printer)
        {
            if (entity.Kind == ChangeKind.Upserted)
            {
                WeakReferenceMessenger.Default.Send(new PrinterEntityUpsert(entity), entity.EntityId);
                WeakReferenceMessenger.Default.Send(new PrinterEntityUpsert(entity));
            }
            else if (entity.Kind == ChangeKind.Deleted)
            {
                WeakReferenceMessenger.Default.Send(new PrinterEntityDelete(entity), entity.EntityId);
                WeakReferenceMessenger.Default.Send(new PrinterEntityDelete(entity));
            }
        }
        if (entity.EntityType == EntityTypes.Workspace)
        {
            WeakReferenceMessenger.Default.Send(new WorkspaceEntityChanged(entity), entity.EntityId);
            WeakReferenceMessenger.Default.Send(new WorkspaceEntityChanged(entity));
        }
    }

    public void Handle(PrinterEventRecorded recorded)
    {
        // Agents report from their own threads, and the history view changes a collection that is bound to the UI.
        var message = new PrinterEventData(recorded);
        Dispatcher.UIThread.Post(() => WeakReferenceMessenger.Default.Send(message, recorded.PrinterId));
    }

    public void Handle(PrinterConsoleLineRecorded recorded)
    {
        // Printers write from their own threads, and the console changes a collection that is bound to the UI.
        var message = new PrinterConsoleData(recorded);
        Dispatcher.UIThread.Post(() => WeakReferenceMessenger.Default.Send(message, recorded.PrinterId));
    }

    public void Handle(PrinterTelemetry telemetryMessage)
    {
        WeakReferenceMessenger.Default.Send(new PrinterTelemetryData(telemetryMessage), telemetryMessage.PrinterId);
    }
}
