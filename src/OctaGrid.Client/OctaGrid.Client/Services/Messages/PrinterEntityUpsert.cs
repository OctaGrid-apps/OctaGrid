using CommunityToolkit.Mvvm.Messaging.Messages;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Services;

public class PrinterEntityUpsert(EntityChanged entity) : ValueChangedMessage<EntityChanged>(entity)
{ }
