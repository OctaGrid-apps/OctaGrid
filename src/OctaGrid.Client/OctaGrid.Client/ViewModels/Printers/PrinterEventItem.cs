using System.Globalization;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.ViewModels;

/// <summary>One row of a printer's history, with its text ready to show.</summary>
public sealed class PrinterEventItem
{
    public PrinterEventItem(PrinterEvent printerEvent)
    {
        Event = printerEvent;
    }

    public PrinterEvent Event { get; }

    public string Message => Event.Message;

    public string SeverityText => Event.Severity.ToString();

    public string TimeText => Event.Timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
