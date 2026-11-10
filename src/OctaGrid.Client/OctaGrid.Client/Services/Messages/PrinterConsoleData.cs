using CommunityToolkit.Mvvm.Messaging.Messages;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Services;

/// <summary>A new console line of a printer, sent on the UI thread with the printer's id as the token.</summary>
public class PrinterConsoleData : ValueChangedMessage<PrinterConsoleLineRecorded>
{
    public PrinterConsoleData(PrinterConsoleLineRecorded recorded)
        : base(recorded)
    {
    }
}
