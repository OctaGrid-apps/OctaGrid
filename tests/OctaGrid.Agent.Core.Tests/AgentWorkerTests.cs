using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Tests;

public class AgentWorkerTests
{
    private sealed class EmptyProfiles : IPrinterProfileSource
    {
        public Task<IReadOnlyList<PrinterProfile>> GetProfilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PrinterProfile>>([]);
    }

    [Fact]
    public async Task Resolves_from_the_container_the_way_the_app_registers_it()
    {
        var connector = new FakeConnector("acme.fake", _ => new MinimalSession());
        var services = new ServiceCollection();
        services.AddSingleton<ITelemetrySink, FakeTelemetry>();
        services.AddSingleton<IPrinterProfileSource, EmptyProfiles>();
        services.AddSingleton(new ConnectorRegistry().Add(connector));
        services.AddSingleton<AgentWorker>();
        await using var provider = services.BuildServiceProvider();

        var worker = provider.GetRequiredService<AgentWorker>();

        Assert.Equal(["acme.fake"], worker.ConnectorDescriptors.Select(d => d.Id));
    }

    [Fact]
    public async Task Connection_test_for_an_unknown_connector_fails_with_a_message()
    {
        var worker = new AgentWorker(new FakeTelemetry(), new EmptyProfiles(), new ConnectorRegistry());

        var result = await worker.TestConnectionAsync("acme.missing", "10.0.0.1", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("acme.missing", result.Error);
    }

    [Fact]
    public async Task Connection_test_uses_the_connector_from_the_registry()
    {
        var connector = new FakeConnector("acme.fake", _ => new MinimalSession());
        var worker = new AgentWorker(
            new FakeTelemetry(), new EmptyProfiles(), new ConnectorRegistry().Add(connector));

        var result = await worker.TestConnectionAsync("acme.fake", "10.0.0.1", "key", CancellationToken.None);

        Assert.True(result.Success);
        var profile = Assert.Single(connector.Created);
        Assert.Equal("10.0.0.1", profile.Get("url"));
        Assert.Equal("key", profile.Get("apiKey"));
    }

    [Fact]
    public async Task Connection_test_leaves_out_an_empty_api_key()
    {
        var connector = new FakeConnector("acme.fake", _ => new MinimalSession());
        var worker = new AgentWorker(
            new FakeTelemetry(), new EmptyProfiles(), new ConnectorRegistry().Add(connector));

        await worker.TestConnectionAsync("acme.fake", "10.0.0.1", "", CancellationToken.None);

        Assert.Null(Assert.Single(connector.Created).Get("apiKey"));
    }
}
