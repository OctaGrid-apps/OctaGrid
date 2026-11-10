using System.Globalization;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.ViewModels;

/// <summary>One row of a printer's console, with its text ready to show.</summary>
public sealed class ConsoleLineItem
{
    public ConsoleLineItem(ConsoleLine line)
    {
        Line = line;
    }

    public ConsoleLine Line { get; }

    public string Text => Line.Text;

    public string TimeText => Line.Timestamp.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
}
