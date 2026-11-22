using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Application.Tests;

/// <summary>The printer aggregate and its secrets: the rules that used to live in the printers service.</summary>
public sealed class PrinterAggregateTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private static Printer NewPrinter(string connectorId = "octagrid.moonraker")
    {
        var printer = Printer.Create(WorkspaceId, "Voron", connectorId, new Dictionary<string, string> { ["url"] = "10.0.0.1" }, enabled: true);
        printer.StoreSecrets("encrypted");
        return printer;
    }

    private static Agent AgentIn(Guid workspaceId) => new() { Id = Guid.NewGuid(), Name = "Agent", WorkspaceId = workspaceId };

    [Fact]
    public void A_new_printer_has_its_id_before_it_is_saved_and_no_agent()
    {
        var printer = Printer.Create(WorkspaceId, "Voron", "octagrid.moonraker", new Dictionary<string, string>(), enabled: false);

        Assert.NotEqual(Guid.Empty, printer.Id);
        Assert.Null(printer.AgentId);
        Assert.Null(printer.Status!.State);
    }

    [Fact]
    public void Another_connector_drops_the_saved_secrets()
    {
        var printer = NewPrinter();

        printer.Reconfigure("Voron", "octagrid.bambu-mqtt", new Dictionary<string, string>(), enabled: true);

        Assert.Null(printer.ProtectedSecrets);
    }

    [Fact]
    public void The_same_connector_in_another_case_keeps_the_saved_secrets()
    {
        var printer = NewPrinter();

        printer.Reconfigure("Renamed", "OctaGrid.Moonraker", new Dictionary<string, string>(), enabled: false);

        Assert.Equal("encrypted", printer.ProtectedSecrets);
        Assert.Equal("Renamed", printer.Name);
        Assert.False(printer.Enabled);
    }

    [Fact]
    public void A_printer_is_assigned_only_to_an_agent_of_its_workspace()
    {
        var printer = NewPrinter();
        var own = AgentIn(WorkspaceId);

        Assert.Throws<ValidationException>(() => printer.AssignTo(AgentIn(Guid.NewGuid())));
        printer.AssignTo(own);

        Assert.Equal(own.Id, printer.AgentId);
    }

    [Fact]
    public void Enabling_reports_whether_anything_changed()
    {
        var printer = NewPrinter();

        Assert.False(printer.SetEnabled(true));
        Assert.True(printer.SetEnabled(false));
    }

    [Fact]
    public void Entered_secrets_replace_remove_or_keep_the_saved_ones()
    {
        var saved = new PrinterSecrets(new Dictionary<string, string> { ["apiKey"] = "old", ["password"] = "keep", ["token"] = "drop" });

        var result = saved.With(new Dictionary<string, string> { ["apiKey"] = "new", ["token"] = "", ["code"] = "added" });

        Assert.Equal(
            new Dictionary<string, string> { ["apiKey"] = "new", ["password"] = "keep", ["code"] = "added" },
            result.Values);
        Assert.Equal(["apiKey", "code", "password"], result.Keys);
    }

    [Fact]
    public void Empty_values_are_never_kept_as_secrets()
    {
        var secrets = new PrinterSecrets(new Dictionary<string, string> { ["apiKey"] = "" });

        Assert.True(secrets.IsEmpty);
        Assert.Same(PrinterSecrets.None, PrinterSecrets.None.With(null));
    }
}
