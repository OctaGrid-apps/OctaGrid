using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Services;

public class PrinterEntityDelete(EntityChanged entity) : ValueChangedMessage<EntityChanged>(entity)
{ }

public class PrinterEntityUpsert(EntityChanged entity) : ValueChangedMessage<EntityChanged>(entity)
{ }

public class WorkspaceEntityChanged(EntityChanged entity) : ValueChangedMessage<EntityChanged>(entity)
{ }

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

    public void Handle(PrinterTelemetry telemetryMessage)
    {
        WeakReferenceMessenger.Default.Send(new PrinterTelemetryData(telemetryMessage), telemetryMessage.PrinterId);
    }
}

public class PrinterTelemetryData(PrinterTelemetry telemetryMessage) : ValueChangedMessage<PrinterTelemetry>(telemetryMessage)
{ }
