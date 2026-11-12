using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Agent.Core.Extensions;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Agent.Core.Tests;

public class AgentWorkerTests
{
    private sealed class FixedProfiles(params PrinterProfile[] profiles) : IPrinterProfileSource
    {
        public Task<IReadOnlyList<PrinterProfile>> GetProfilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PrinterProfile>>(profiles);
    }

    private static (AgentWorker Worker, FakeConnector Connector, List<FakeSession> Sessions, InMemoryStateStore Store) Create(
        params PrinterProfile[] profiles)
    {
        var sessions = new List<FakeSession>();
        var connector = new FakeConnector("acme.fake", _ =>
        {
            var session = new FakeSession();
            sessions.Add(session);
            return session;
        });
        var store = new InMemoryStateStore();
        var extensions = new ExtensionManager(new ConnectorRegistry().Add(connector), store);
        return (new AgentWorker(new FakeTelemetry(), new FixedProfiles(profiles), extensions), connector, sessions, store);
    }

    private static Dictionary<string, string> Settings(string url, string? apiKey = null)
    {
        var settings = new Dictionary<string, string> { ["url"] = url };
        if (apiKey is not null)
        {
            settings["apiKey"] = apiKey;
        }

        return settings;
    }

    private static PrinterProfile Printer(string connectorId = "acme.fake") =>
        new(Guid.NewGuid(), connectorId, new Dictionary<string, string> { ["url"] = "10.0.0.1" });

    private sealed class MutableProfiles : IPrinterProfileSource
    {
        public List<PrinterProfile> Profiles { get; } = [];

        public Task<IReadOnlyList<PrinterProfile>> GetProfilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PrinterProfile>>(Profiles.ToList());
    }

    [Fact]
    public async Task What_a_session_reports_is_kept_as_history_until_the_printer_is_deleted()
    {
        var sessions = new List<FakeSession>();
        var connector = new FakeConnector("acme.fake", _ =>
        {
            var session = new FakeSession();
            sessions.Add(session);
            return session;
        });
        var printer = Printer();
        var profiles = new MutableProfiles { Profiles = { printer } };
        var worker = new AgentWorker(
            new FakeTelemetry(), profiles, new ExtensionManager(new ConnectorRegistry().Add(connector), new InMemoryStateStore()));

        await worker.ExecuteAsync(CancellationToken.None);
        await sessions[0].Started;
        sessions[0].Telemetry!.Send(new PrinterTelemetry(printer.PrinterId) { State = UniversalPrinterState.Printing });

        var page = worker.GetPrinterEvents(printer.PrinterId, null, 10);
        Assert.Equal("State changed to Printing.", Assert.Single(page.Events).Message);

        profiles.Profiles.Clear();
        await worker.RefreshProfilesAsync(CancellationToken.None);

        Assert.Empty(worker.GetPrinterEvents(printer.PrinterId, null, 10).Events);
        await worker.DisposeAsync();
    }

    [Fact]
    public async Task Events_are_announced_to_whoever_subscribed_with_the_printer_they_belong_to()
    {
        var sessions = new List<FakeSession>();
        var connector = new FakeConnector("acme.fake", _ =>
        {
            var session = new FakeSession();
            sessions.Add(session);
            return session;
        });
        var printer = Printer();
        var worker = new AgentWorker(
            new FakeTelemetry(), new FixedProfiles(printer), new ExtensionManager(new ConnectorRegistry().Add(connector), new InMemoryStateStore()));
        var announced = new List<PrinterEventRecorded>();
        worker.EventRecorded += announced.Add;

        await worker.ExecuteAsync(CancellationToken.None);
        await sessions[0].Started;
        sessions[0].Telemetry!.Send(new PrinterTelemetry(printer.PrinterId) { State = UniversalPrinterState.Paused });

        var notice = Assert.Single(announced);
        Assert.Equal(printer.PrinterId, notice.PrinterId);
        Assert.Equal(worker.GetPrinterEvents(printer.PrinterId, null, 10).HistoryId, notice.HistoryId);

        worker.EventRecorded -= announced.Add;
        sessions[0].Telemetry!.Send(new PrinterTelemetry(printer.PrinterId) { State = UniversalPrinterState.Printing });

        Assert.Single(announced);
        await worker.DisposeAsync();
    }

    [Fact]
    public async Task Resolves_from_the_container_the_way_the_app_registers_it()
    {
        var connector = new FakeConnector("acme.fake", _ => new MinimalSession());
        var services = new ServiceCollection();
        services.AddSingleton<ITelemetrySink, FakeTelemetry>();
        services.AddSingleton<IPrinterProfileSource>(new FixedProfiles());
        services.AddSingleton(new ConnectorRegistry().Add(connector));
        services.AddSingleton<IExtensionStateStore, InMemoryStateStore>();
        services.AddSingleton<ExtensionManager>();
        services.AddSingleton<AgentWorker>();
        await using var provider = services.BuildServiceProvider();

        var worker = provider.GetRequiredService<AgentWorker>();

        Assert.Equal(["acme.fake"], worker.ConnectorDescriptors.Select(d => d.Id));
    }

    [Fact]
    public async Task Connection_test_for_an_unknown_connector_fails_with_a_message()
    {
        var (worker, _, _, _) = Create();

        var result = await worker.TestConnectionAsync("acme.missing", Settings("10.0.0.1"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("acme.missing", result.Error);
    }

    [Fact]
    public async Task Connection_test_uses_the_connector_from_the_registry()
    {
        var (worker, connector, _, _) = Create();

        var result = await worker.TestConnectionAsync("acme.fake", Settings("10.0.0.1", "key"), CancellationToken.None);

        Assert.True(result.Success);
        var profile = Assert.Single(connector.Created);
        Assert.Equal("10.0.0.1", profile.Get("url"));
        Assert.Equal("key", profile.Get("apiKey"));
    }

    [Fact]
    public async Task Connection_test_leaves_out_an_empty_api_key()
    {
        var (worker, connector, _, _) = Create();

        await worker.TestConnectionAsync("acme.fake", Settings("10.0.0.1", ""), CancellationToken.None);

        Assert.Null(Assert.Single(connector.Created).Get("apiKey"));
    }

    [Fact]
    public async Task Disabling_an_extension_stops_its_printers_and_hides_it_from_clients()
    {
        var (worker, _, sessions, _) = Create(Printer());
        await worker.ExecuteAsync(CancellationToken.None);
        Assert.Single(sessions);

        await worker.SetExtensionEnabledAsync("acme.fake", false, CancellationToken.None);

        Assert.True(sessions.Single().Disposed);
        Assert.Empty(worker.ConnectorDescriptors);
        Assert.Equal(ExtensionState.Disabled, Assert.Single(worker.GetExtensions()).State);
    }

    [Fact]
    public async Task Enabling_an_extension_again_restarts_its_printers()
    {
        var (worker, connector, sessions, _) = Create(Printer());
        await worker.ExecuteAsync(CancellationToken.None);
        await worker.SetExtensionEnabledAsync("acme.fake", false, CancellationToken.None);

        await worker.SetExtensionEnabledAsync("acme.fake", true, CancellationToken.None);

        Assert.Equal(2, connector.Created.Count);
        Assert.False(sessions[1].Disposed);
        Assert.Single(worker.ConnectorDescriptors);
        await worker.DisposeAsync();
    }

    [Fact]
    public async Task A_disabled_extension_is_not_started_at_launch()
    {
        var (worker, connector, _, store) = Create(Printer());
        store.SaveDisabled(["acme.fake"]);
        var restarted = new AgentWorker(
            new FakeTelemetry(),
            new FixedProfiles(Printer()),
            new ExtensionManager(new ConnectorRegistry().Add(connector), store));

        await restarted.ExecuteAsync(CancellationToken.None);

        Assert.Empty(connector.Created);
        await restarted.DisposeAsync();
        await worker.DisposeAsync();
    }

    [Fact]
    public async Task Connection_test_for_a_disabled_connector_says_so()
    {
        var (worker, connector, _, _) = Create();
        await worker.SetExtensionEnabledAsync("acme.fake", false, CancellationToken.None);

        var result = await worker.TestConnectionAsync("acme.fake", Settings("10.0.0.1"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("disabled", result.Error);
        Assert.Empty(connector.Created);
    }

    [Fact]
    public async Task Changing_an_unknown_extension_changes_nothing()
    {
        var (worker, _, _, store) = Create();

        await worker.SetExtensionEnabledAsync("acme.missing", false, CancellationToken.None);

        Assert.Empty(store.LoadDisabled());
    }

    [Fact]
    public async Task Connection_test_passes_every_entered_setting_to_the_connector()
    {
        var (worker, connector, _, _) = Create();
        var settings = new Dictionary<string, string>
        {
            ["url"] = "10.0.0.1",
            ["serialNumber"] = "01P",
            ["accessCode"] = "123456",
            ["empty"] = "",
        };

        await worker.TestConnectionAsync("acme.fake", settings, CancellationToken.None);

        var profile = Assert.Single(connector.Created);
        Assert.Equal("01P", profile.Get("serialNumber"));
        Assert.Equal("123456", profile.Get("accessCode"));
        Assert.Null(profile.Get("empty"));
    }

    [Fact]
    public async Task Reports_how_many_printers_use_each_extension()
    {
        var (worker, _, _, _) = Create(Printer(), Printer());
        await worker.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, Assert.Single(worker.GetExtensions()).ActivePrinters);

        await worker.SetExtensionEnabledAsync("acme.fake", false, CancellationToken.None);

        Assert.Equal(0, Assert.Single(worker.GetExtensions()).ActivePrinters);
    }
}
