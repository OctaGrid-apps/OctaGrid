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
}
