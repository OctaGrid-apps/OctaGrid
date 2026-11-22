using OctaGrid.Application.Agents;
using OctaGrid.Application.Events;
using OctaGrid.Application.Services;
using OctaGrid.Data.Events;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.AgentLink.Notifications;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Application.Tests;

/// <summary>
/// What every link shares: the handshake that decides whether an agent may be reached, the channels its
/// notifications travel on, and what the backend does with an agent's connection state and connectors.
/// </summary>
public sealed class AgentLinkTests : IDisposable
{
    private static readonly Guid AgentId = Guid.NewGuid();

    private readonly AgentLinkFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void An_agent_that_speaks_the_backends_protocol_is_accepted()
    {
        var result = AgentHandshake.Answer(new AgentHello(AgentId, "1.0.0", ProtocolVersion: 3), backendVersion: 3);

        Assert.Equal(new AgentHelloResult(AgentHelloStatus.Accepted, 3), result);
        Assert.Equal(AgentConnectionState.Online, AgentHandshake.ToConnectionState(result.Status));
    }

    [Fact]
    public void An_older_agent_is_told_to_update()
    {
        var result = AgentHandshake.Answer(new AgentHello(AgentId, "1.0.0", ProtocolVersion: 2), backendVersion: 3);

        Assert.Equal(AgentHelloStatus.AgentUpdateRequired, result.Status);
        Assert.Equal(3, result.ProtocolVersion);
        Assert.Contains("Update the agent", result.Message);
        Assert.Equal(AgentConnectionState.AgentUpdateRequired, AgentHandshake.ToConnectionState(result.Status));
    }

    [Fact]
    public void A_newer_agent_is_told_that_the_app_has_to_update()
    {
        var result = AgentHandshake.Answer(new AgentHello(AgentId, "2.0.0", ProtocolVersion: 4), backendVersion: 3);

        Assert.Equal(AgentHelloStatus.BackendUpdateRequired, result.Status);
        Assert.Contains("Update the app", result.Message);
        Assert.Equal(AgentConnectionState.BackendUpdateRequired, AgentHandshake.ToConnectionState(result.Status));
    }

    [Theory]
    [InlineData(AgentConnectionState.Offline, "not connected")]
    [InlineData(AgentConnectionState.AgentUpdateRequired, "Update the agent")]
    [InlineData(AgentConnectionState.BackendUpdateRequired, "Update the app")]
    public void An_unreachable_agent_is_reported_with_what_the_user_can_do(AgentConnectionState state, string advice)
    {
        var error = AgentHandshake.Unavailable(AgentId, state);

        Assert.Equal(AgentId, error.AgentId);
        Assert.Equal(AgentUnavailableException.Uri, error.TypeUri);
        Assert.Contains(advice, error.Message);
    }

    [Fact]
    public void Telemetry_and_events_reach_only_the_listeners_of_their_channel()
    {
        var fanOut = new AgentNotificationFanOut();
        using var events = fanOut.Subscribe(AgentNotificationChannel.Events);
        using var telemetry = fanOut.Subscribe(AgentNotificationChannel.Telemetry);
        var printerId = Guid.NewGuid();

        fanOut.Publish(new AgentNotificationEnvelope(AgentId, new PrinterTelemetryReported(new PrinterTelemetry(printerId), null)));
        fanOut.Publish(new AgentNotificationEnvelope(AgentId, new ConnectorsChanged([])));

        Assert.True(telemetry.Reader.TryRead(out var snapshot));
        Assert.IsType<PrinterTelemetryReported>(snapshot.Notification);
        Assert.False(telemetry.Reader.TryRead(out _));
        Assert.True(events.Reader.TryRead(out var changed));
        Assert.IsType<ConnectorsChanged>(changed.Notification);
        Assert.False(events.Reader.TryRead(out _));
    }

    [Fact]
    public void A_reader_that_falls_behind_gets_the_latest_telemetry_of_each_printer_and_every_event()
    {
        var fanOut = new AgentNotificationFanOut();
        using var events = fanOut.Subscribe(AgentNotificationChannel.Events);
        using var telemetry = fanOut.Subscribe(AgentNotificationChannel.Telemetry);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        fanOut.Publish(Telemetry(first, nozzle: 1));
        for (var i = 0; i < 100; i++)
        {
            fanOut.Publish(Telemetry(second, nozzle: i));
            fanOut.Publish(new AgentNotificationEnvelope(AgentId,
                new PrinterStateChanged(second, null, UniversalPrinterState.Idle, $"{i}", DateTimeOffset.UnixEpoch)));
        }

        Assert.Equal(
            [(first, 1d), (second, 99d)],
            ReadAll(telemetry).Select(r => (r.Telemetry.PrinterId, r.Telemetry.NozzleTemperature!.Value)));
        var kept = 0;
        while (events.Reader.TryRead(out _))
        {
            kept++;
        }

        Assert.Equal(100, kept);
    }

    [Fact]
    public void A_snapshot_that_replaces_a_waiting_one_keeps_its_history_sample()
    {
        var fanOut = new AgentNotificationFanOut();
        using var telemetry = fanOut.Subscribe(AgentNotificationChannel.Telemetry);
        var printerId = Guid.NewGuid();
        var sample = new TelemetrySample(DateTimeOffset.UnixEpoch, []);

        fanOut.Publish(Telemetry(printerId, nozzle: 200, sample));
        fanOut.Publish(Telemetry(printerId, nozzle: 201));

        var latest = Assert.Single(ReadAll(telemetry));
        Assert.Equal(201, latest.Telemetry.NozzleTemperature);
        Assert.Equal(sample, latest.Sample);
    }

    private static AgentNotificationEnvelope Telemetry(Guid printerId, double nozzle, TelemetrySample? sample = null) =>
        new(AgentId, new PrinterTelemetryReported(new PrinterTelemetry(printerId) { NozzleTemperature = nozzle }, sample));

    private static List<PrinterTelemetryReported> ReadAll(AgentNotificationSubscription subscription)
    {
        var read = new List<PrinterTelemetryReported>();
        while (subscription.Reader.TryRead(out var envelope))
        {
            read.Add((PrinterTelemetryReported)envelope.Notification);
        }

        return read;
    }

    [Fact]
    public void A_disposed_subscription_gets_nothing_more_and_its_reader_completes()
    {
        var fanOut = new AgentNotificationFanOut();
        var events = fanOut.Subscribe(AgentNotificationChannel.Events);

        events.Dispose();
        fanOut.Publish(new AgentNotificationEnvelope(AgentId, new ConnectorsChanged([])));

        Assert.False(events.Reader.TryRead(out _));
        Assert.True(events.Reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task The_agent_list_says_which_agents_are_connected()
    {
        _fixture.Link.Unreachable.Add(_fixture.AgentId);

        await using var context = _fixture.NewContext();
        var agents = await new AgentsService(context, TestAccess.For(context), _fixture.Link)
            .GetList(_fixture.UserId, _fixture.WorkspaceId, CancellationToken.None);

        Assert.Equal(AgentConnectionState.Offline, Assert.Single(agents).Connection);

        _fixture.Link.Unreachable.Clear();
        agents = await new AgentsService(context, TestAccess.For(context), _fixture.Link)
            .GetList(_fixture.UserId, _fixture.WorkspaceId, CancellationToken.None);

        Assert.Equal(AgentConnectionState.Online, Assert.Single(agents).Connection);
    }

    [Fact]
    public async Task The_connectors_are_those_of_the_agent_asked_for()
    {
        var connector = new ConnectorDescriptor("acme.printer", "Acme", "1.0.0", "Acme", ConnectorCapabilities.None, [], []);
        _fixture.Link.Connectors.Add(connector);

        await using var context = _fixture.NewContext();
        var connectors = await Commands(context).GetConnectors(_fixture.UserId, _fixture.AgentId, CancellationToken.None);

        Assert.Equal([connector], connectors);
        var (agentId, request) = Assert.Single(_fixture.Link.Sent);
        Assert.Equal(_fixture.AgentId, agentId);
        Assert.IsType<GetConnectorsRequest>(request);
    }

    [Fact]
    public async Task The_connectors_of_an_agent_in_another_workspace_cannot_be_read()
    {
        var foreign = _fixture.AddForeignAgent();

        await using var context = _fixture.NewContext();
        await Assert.ThrowsAsync<AuthenticationException>(
            () => Commands(context).GetConnectors(_fixture.UserId, foreign, CancellationToken.None));
        Assert.Empty(_fixture.Link.Sent);
    }

    [Fact]
    public async Task Asking_an_agent_that_is_not_connected_fails_as_unavailable()
    {
        _fixture.Link.Unreachable.Add(_fixture.AgentId);

        await using var context = _fixture.NewContext();
        await Assert.ThrowsAsync<AgentUnavailableException>(
            () => Commands(context).GetConnectors(_fixture.UserId, _fixture.AgentId, CancellationToken.None));
    }

    [Fact]
    public async Task A_saved_printer_change_sends_every_affected_agent_its_printers()
    {
        var printer = await _fixture.CreatePrinter();
        var other = Guid.NewGuid();
        _fixture.Link.Sent.Clear();

        await using var context = _fixture.NewContext();
        var publisher = Publisher(context);
        await publisher.HandleAsync(new PrinterChanged(printer.Id, _fixture.WorkspaceId), CancellationToken.None);
        await publisher.HandleAsync(new PrinterAgentChanged(printer.Id, _fixture.WorkspaceId, _fixture.AgentId, other), CancellationToken.None);

        Assert.Equal([_fixture.AgentId, _fixture.AgentId, other], _fixture.Link.Sent.Select(s => s.AgentId));
        Assert.All(_fixture.Link.Sent, s => Assert.IsType<ApplyPrinterProfilesRequest>(s.Request));
    }

    [Fact]
    public async Task An_agent_that_is_not_connected_is_not_retried_and_gets_its_printers_when_it_connects()
    {
        var printer = await _fixture.CreatePrinter();
        _fixture.Link.Unreachable.Add(_fixture.AgentId);

        await using var context = _fixture.NewContext();
        await Publisher(context).HandleAsync(new PrinterRemoved(printer.Id, _fixture.WorkspaceId, _fixture.AgentId), CancellationToken.None);

        Assert.Empty(_fixture.Link.Sent);
    }

    [Fact]
    public async Task An_agent_that_refuses_its_printers_does_not_fail_the_event()
    {
        var printer = await _fixture.CreatePrinter();
        _fixture.Link.Refused = typeof(ApplyPrinterProfilesRequest);

        await using var context = _fixture.NewContext();
        var exception = await Record.ExceptionAsync(() =>
            Publisher(context).HandleAsync(new PrinterChanged(printer.Id, _fixture.WorkspaceId), CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task A_change_that_concerns_no_printer_sends_nothing()
    {
        await using var context = _fixture.NewContext();
        await Publisher(context).HandleAsync(new WorkspaceRenamed(_fixture.WorkspaceId, "Renamed"), CancellationToken.None);

        Assert.Empty(_fixture.Link.Sent);
    }

    private PrinterCommandsService Commands(Data.OctaGridContext context) =>
        new(TestAccess.For(context), _fixture.Printers(context), AgentLinkFixture.Routing(context), _fixture.Link);

    private AgentProfilesPublisher Publisher(Data.OctaGridContext context) =>
        new(context, new AgentProfilesService(_fixture.Printers(context), _fixture.Link));
}
