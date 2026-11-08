using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Tests;

public class PrinterStatusWidgetTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Print_controls_need_an_available_connector(bool connectorAvailable, bool expected)
    {
        var widget = new PrinterStatusWidget { IsConnectorAvailable = connectorAvailable, State = UniversalPrinterState.Printing };

        Assert.Equal(expected, widget.CanStartPause);
        Assert.Equal(expected, widget.CanStop);
    }

    [Fact]
    public void Changing_the_connector_availability_updates_the_print_controls()
    {
        var widget = new PrinterStatusWidget { State = UniversalPrinterState.Printing };
        var changed = new List<string?>();
        widget.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        widget.IsConnectorAvailable = false;

        Assert.Contains(nameof(PrinterStatusWidget.IsConnectorAvailable), changed);
        Assert.Contains(nameof(PrinterStatusWidget.CanStartPause), changed);
        Assert.Contains(nameof(PrinterStatusWidget.CanStop), changed);
        Assert.False(widget.CanStop);
    }
}
