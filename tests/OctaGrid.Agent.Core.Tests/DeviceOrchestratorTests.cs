using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Tests;

public class DeviceOrchestratorTests
{
    private const string Id = "acme.fake";

    private static PrinterProfile Profile(Guid? id = null, string connectorId = Id, string url = "10.0.0.1",
        string? apiKey = null)
    {
        var settings = new Dictionary<string, string> { ["url"] = url };
        if (apiKey is not null)
        {
            settings["apiKey"] = apiKey;
        }

        return new PrinterProfile(id ?? Guid.NewGuid(), connectorId, settings);
    }

    private static (DeviceOrchestrator Orchestrator, FakeConnector Connector, List<FakeSession> Sessions, FakeTelemetry Telemetry)
        Create()
    {
        var sessions = new List<FakeSession>();
        var connector = new FakeConnector(Id, _ =>
        {
            var session = new FakeSession();
            sessions.Add(session);
            return session;
        });
        var telemetry = new FakeTelemetry();
        var orchestrator = new DeviceOrchestrator(
            new ConnectorRegistry().Add(connector), telemetry, dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"));
        return (orchestrator, connector, sessions, telemetry);
    }

    [Fact]
    public async Task Starts_a_session_with_the_profile_and_the_telemetry_sink()
    {
        var (orchestrator, connector, sessions, telemetry) = Create();
        var profile = Profile(apiKey: "secret");

        await orchestrator.ApplyProfilesAsync([profile]);
        await sessions.Single().Started;

        Assert.Same(profile, Assert.Single(connector.Created));
        Assert.Same(telemetry, sessions.Single().Telemetry);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Does_not_restart_when_an_equal_profile_arrives_with_new_dictionary_instances()
    {
        var (orchestrator, connector, sessions, _) = Create();
        var id = Guid.NewGuid();

        await orchestrator.ApplyProfilesAsync([Profile(id, apiKey: "k")]);
        await orchestrator.ApplyProfilesAsync([Profile(id, apiKey: "k")]);

        Assert.Single(connector.Created);
        Assert.False(sessions.Single().Disposed);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Restarts_when_the_endpoint_or_settings_change()
    {
        var (orchestrator, connector, sessions, _) = Create();
        var id = Guid.NewGuid();

        await orchestrator.ApplyProfilesAsync([Profile(id, url: "10.0.0.1")]);
        await orchestrator.ApplyProfilesAsync([Profile(id, url: "10.0.0.2")]);
        await orchestrator.ApplyProfilesAsync([Profile(id, url: "10.0.0.2", apiKey: "new")]);

        Assert.Equal(3, connector.Created.Count);
        Assert.True(sessions[0].Disposed);
        Assert.True(sessions[1].Disposed);
        Assert.False(sessions[2].Disposed);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Stops_sessions_for_printers_that_are_no_longer_wanted()
    {
        var (orchestrator, _, sessions, _) = Create();

        await orchestrator.ApplyProfilesAsync([Profile()]);
        await orchestrator.ApplyProfilesAsync([]);

        Assert.True(sessions.Single().Disposed);
    }

    [Fact]
    public async Task Disposing_stops_all_sessions()
    {
        var (orchestrator, _, sessions, _) = Create();
        await orchestrator.ApplyProfilesAsync([Profile(), Profile()]);

        await orchestrator.DisposeAsync();

        Assert.All(sessions, s => Assert.True(s.Disposed));
    }

    [Fact]
    public async Task A_printer_with_a_missing_connector_does_not_stop_the_others()
    {
        var (orchestrator, connector, _, _) = Create();
        var good = Profile();

        await orchestrator.ApplyProfilesAsync([Profile(connectorId: "acme.missing"), good]);

        Assert.Same(good, Assert.Single(connector.Created));
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Routes_operations_to_the_session_capabilities()
    {
        var (orchestrator, _, sessions, _) = Create();
        var profile = Profile();
        var ct = CancellationToken.None;
        await orchestrator.ApplyProfilesAsync([profile]);

        await orchestrator.StartPrintJobAsync(profile.PrinterId, "a.gcode", ct);
        await orchestrator.PausePrintJobAsync(profile.PrinterId, ct);
        await orchestrator.ResumePrintJobAsync(profile.PrinterId, ct);
        await orchestrator.StopPrintJobAsync(profile.PrinterId, ct);
        await orchestrator.UploadFileAsync(profile.PrinterId, new MemoryStream(), "b.gcode", ct);
        await orchestrator.DeleteFileAsync(profile.PrinterId, "c.gcode", ct);
        await orchestrator.TriggerInitialStateAsync(profile.PrinterId, ct);
        await orchestrator.TriggerInitialStateAsync([profile.PrinterId], ct);
        var files = await orchestrator.GetPrinterFilesAsync(profile.PrinterId, ct);
        var thumbnail = await orchestrator.GetThumbnailAsync(profile.PrinterId, "a.gcode", ct);

        Assert.Equal(
            ["start:a.gcode", "pause", "resume", "stop", "upload:b.gcode", "delete:c.gcode", "state", "state", "list", "thumb:a.gcode"],
            sessions.Single().Calls);
        Assert.Single(files);
        Assert.Equal("image/png", thumbnail?.ContentType);
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Operations_for_an_unknown_device_do_nothing()
    {
        var (orchestrator, _, _, _) = Create();
        var unknown = Guid.NewGuid();
        var ct = CancellationToken.None;

        await orchestrator.StartPrintJobAsync(unknown, "a.gcode", ct);
        await orchestrator.PausePrintJobAsync(unknown, ct);
        await orchestrator.TriggerInitialStateAsync(unknown, ct);

        Assert.Empty(await orchestrator.GetPrinterFilesAsync(unknown, ct));
        Assert.Null(await orchestrator.GetThumbnailAsync(unknown, "a.gcode", ct));
    }

    [Fact]
    public async Task A_connector_without_a_capability_is_skipped_not_failed()
    {
        var connector = new FakeConnector(Id, _ => new MinimalSession());
        var orchestrator = new DeviceOrchestrator(
            new ConnectorRegistry().Add(connector), new FakeTelemetry(),
            dataRoot: Path.Combine(Path.GetTempPath(), "octagrid-tests"));
        var profile = Profile();
        var ct = CancellationToken.None;
        await orchestrator.ApplyProfilesAsync([profile]);

        await orchestrator.StartPrintJobAsync(profile.PrinterId, "a.gcode", ct);
        await orchestrator.DeleteFileAsync(profile.PrinterId, "a.gcode", ct);

        Assert.Empty(await orchestrator.GetPrinterFilesAsync(profile.PrinterId, ct));
        Assert.Null(await orchestrator.GetThumbnailAsync(profile.PrinterId, "a.gcode", ct));
        await orchestrator.DisposeAsync();
    }
}
