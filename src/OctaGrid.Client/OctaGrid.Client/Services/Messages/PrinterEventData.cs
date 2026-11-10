using CommunityToolkit.Mvvm.Messaging.Messages;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Services;

/// <summary>A new printer event, sent on the UI thread with the printer's id as the token.</summary>
public class PrinterEventData : ValueChangedMessage<PrinterEventRecorded>
{
    public PrinterEventData(PrinterEventRecorded recorded)
        : base(recorded)
    {
    }
}
