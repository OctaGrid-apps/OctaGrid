using OctaGrid.Client.Contracts;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.Services;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Tests;

/// <summary>Each agent has its own connectors; the catalog keeps them per agent and names them for the views.</summary>
public sealed class ConnectorCatalogTests
{
    private static readonly ConnectorDescriptor Bambu = Connector("octagrid.bambu-mqtt", "Bambu Lab");
    private static readonly ConnectorDescriptor Moonraker = Connector("octagrid.moonraker", "Moonraker");

    private static ConnectorDescriptor Connector(string id, string name) =>
        new(id, name, "1.0.0", "OctaGrid", ConnectorCapabilities.None, [], []);

    private static ConnectorCatalog NewCatalog(IAgentCommandsService? commands = null)
    {
        var design = new DesignOctaGridClient();
        return new ConnectorCatalog(commands ?? new DesignAgentCommands(), new AgentDirectory(design, design));
    }

    [Fact]
    public void The_connectors_of_each_agent_are_kept_apart_and_listed_once()
    {
        var catalog = NewCatalog();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        catalog.Set(first, [Bambu]);
        catalog.Set(second, [Bambu, Moonraker]);

        Assert.Equal([Bambu], catalog.ForAgent(first));
        Assert.Equal([Bambu, Moonraker], catalog.ForAgent(second));
        Assert.Equal(["octagrid.bambu-mqtt", "octagrid.moonraker"], catalog.Connectors.Select(c => c.Id).Order());
        Assert.Equal("Moonraker", catalog.Find("OCTAGRID.MOONRAKER")?.DisplayName);
        Assert.Null(catalog.SoleAgentId);
    }

    [Fact]
    public void With_one_agent_it_is_the_one_that_tests_a_new_printer()
    {
        var catalog = NewCatalog();
        var agentId = Guid.NewGuid();

        catalog.Set(agentId, [Bambu]);

        Assert.Equal(agentId, catalog.SoleAgentId);
    }

    [Fact]
    public async Task Reading_an_agent_again_replaces_its_connectors()
    {
        var commands = new ConnectorsByAgent();
        var catalog = NewCatalog(commands);
        var agentId = Guid.NewGuid();
        catalog.Set(agentId, [Bambu]);

        commands.Connectors[agentId] = [Moonraker];
        await catalog.RefreshAsync(agentId, CancellationToken.None);

        Assert.Equal([Moonraker], catalog.ForAgent(agentId));
    }

    [Fact]
    public async Task An_agent_that_cannot_be_reached_fails_the_refresh_of_that_agent()
    {
        var catalog = NewCatalog(new ConnectorsByAgent());

        await Assert.ThrowsAsync<AgentUnavailableException>(() => catalog.RefreshAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private sealed class ConnectorsByAgent : IAgentCommandsService
    {
        public Dictionary<Guid, IReadOnlyList<ConnectorDescriptor>> Connectors { get; } = [];

        public Task<IReadOnlyList<ConnectorDescriptor>> GetConnectorsAsync(Guid agentId, CancellationToken cancellationToken) =>
            Connectors.TryGetValue(agentId, out var connectors)
                ? Task.FromResult(connectors)
                : throw new AgentUnavailableException(agentId, "The agent is not connected.");

        public Task DeleteFileAsync(Guid printerId, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConsolePage> GetPrinterConsoleAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PrinterEventPage> GetPrinterEventsAsync(Guid printerId, long? afterSequence, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IEnumerable<PrintMetadata>> GetPrinterFilesAsync(Guid printerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PrintThumbnail?> GetThumbnailAsync(Guid printerId, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task PausePrintJobAsync(Guid printerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ResumePrintJobAsync(Guid printerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SendConsoleCommandAsync(Guid printerId, string command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StartPrintJobAsync(Guid printerId, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task StopPrintJobAsync(Guid printerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConnectionTestResult> TestConnectionAsync(
            string connectorId, IReadOnlyDictionary<string, string> settings, Guid? printerId, Guid? agentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UploadFileAsync(Guid printerId, Stream stream, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
