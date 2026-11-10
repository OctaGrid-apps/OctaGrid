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
    public void Conditions_are_shown_most_serious_first()
    {
        var widget = new PrinterStatusWidget();

        widget.SetConditions(
        [
            new PrinterCondition("a", PrinterSeverity.Info, "minor"),
            new PrinterCondition("b", PrinterSeverity.Error, "major")
        ]);

        Assert.Equal(["major", "minor"], widget.ConditionMessages);
        Assert.Equal("major", widget.ConditionMessage);
    }

    [Fact]
    public void Setting_the_conditions_it_already_shows_changes_nothing()
    {
        var widget = new PrinterStatusWidget();
        IReadOnlyList<PrinterCondition> conditions = [new PrinterCondition("a", PrinterSeverity.Warning, "careful")];
        widget.SetConditions(conditions);
        var shown = widget.ConditionMessages;
        var changed = new List<string?>();
        widget.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        widget.SetConditions([new PrinterCondition("a", PrinterSeverity.Warning, "careful")]);

        Assert.Same(shown, widget.ConditionMessages);
        Assert.Empty(changed);
    }

    [Fact]
    public void A_condition_that_goes_away_is_removed_and_listeners_are_told()
    {
        var widget = new PrinterStatusWidget();
        widget.SetConditions([new PrinterCondition("a", PrinterSeverity.Warning, "careful")]);
        var changed = new List<string?>();
        widget.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        widget.SetConditions([]);

        Assert.Empty(widget.ConditionMessages);
        Assert.Null(widget.ConditionMessage);
        Assert.Contains(nameof(PrinterStatusWidget.ConditionMessage), changed);
    }

    [Fact]
    public void No_conditions_on_a_widget_that_shows_none_does_no_work()
    {
        var widget = new PrinterStatusWidget();
        var shown = widget.ConditionMessages;

        widget.SetConditions([]);

        Assert.Same(shown, widget.ConditionMessages);
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
