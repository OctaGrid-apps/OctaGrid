using OctaGrid.Connectors.Moonraker;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Connectors.Tests;

public class MoonrakerStateTests
{
    [Theory]
    [InlineData("standby", "ready", UniversalPrinterState.Idle)]
    [InlineData("printing", "ready", UniversalPrinterState.Printing)]
    [InlineData("paused", "ready", UniversalPrinterState.Paused)]
    [InlineData("complete", "ready", UniversalPrinterState.Completed)]
    [InlineData("cancelled", "ready", UniversalPrinterState.Cancelled)]
    [InlineData("error", "ready", UniversalPrinterState.Failed)]
    public void A_ready_printer_shows_the_job_state(string job, string printer, UniversalPrinterState expected) =>
        Assert.Equal(expected, MoonrakerJson.ToPrinterState(job, printer));

    [Theory]
    [InlineData("printing", "shutdown")]
    [InlineData("standby", "error")]
    [InlineData("complete", "shutdown")]
    public void A_faulted_printer_is_an_error_whatever_the_job_says(string job, string printer) =>
        Assert.Equal(UniversalPrinterState.Error, MoonrakerJson.ToPrinterState(job, printer));

    [Fact]
    public void A_starting_printer_is_offline() =>
        Assert.Equal(UniversalPrinterState.Offline, MoonrakerJson.ToPrinterState("standby", "startup"));

    [Theory]
    [InlineData(null, null, UniversalPrinterState.Idle)]
    [InlineData("printing", null, UniversalPrinterState.Printing)]
    [InlineData(null, "ready", UniversalPrinterState.Idle)]
    [InlineData("unexpected", "ready", UniversalPrinterState.Idle)]
    public void Unknown_or_missing_states_fall_back_sensibly(string? job, string? printer, UniversalPrinterState expected) =>
        Assert.Equal(expected, MoonrakerJson.ToPrinterState(job, printer));

    [Fact]
    public void State_names_are_case_insensitive() =>
        Assert.Equal(UniversalPrinterState.Printing, MoonrakerJson.ToPrinterState("PRINTING", "READY"));
}
