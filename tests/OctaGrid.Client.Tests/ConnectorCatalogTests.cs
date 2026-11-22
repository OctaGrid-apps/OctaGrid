using System.Threading.Channels;
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

    private static ConnectorCatalog NewCatalog(IAgentCommandsService? commands = null, TimeProvider? time = null)
    {
        var design = new DesignOctaGridClient();
        return new ConnectorCatalog(commands ?? new DesignAgentCommands(), new AgentDirectory(design, design), time: time);
    }

    [Fact]
    public async Task Loading_tries_again_with_a_growing_delay_until_every_agent_was_read()
    {
        var commands = new ConnectorsByAgent();
        var timers = new ManualTimers();
        var catalog = NewCatalog(commands, timers);
        var cancellationToken = TestContext.Current.CancellationToken;

        var loading = catalog.LoadAsync(cancellationToken);
        var first = await timers.NextAsync(cancellationToken);
        first.Fire();
        var second = await timers.NextAsync(cancellationToken);
        commands.Connectors[DesignOctaGridClient.DesignAgentId] = [Bambu];
        second.Fire();
        await loading;

        Assert.Equal(ConnectorCatalog.FirstRetryDelay, first.DueTime);
        Assert.Equal(ConnectorCatalog.FirstRetryDelay * 2, second.DueTime);
        Assert.Equal([Bambu], catalog.ForAgent(DesignOctaGridClient.DesignAgentId));
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

    /// <summary>Timers that fire only when a test fires them.</summary>
    private sealed class ManualTimers : TimeProvider
    {
        private readonly Channel<ManualTimer> _created = Channel.CreateUnbounded<ManualTimer>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            _created.Writer.TryWrite(timer);
            return timer;
        }

        /// <summary>Waits for the next timer to be created.</summary>
        public ValueTask<ManualTimer> NextAsync(CancellationToken cancellationToken) => _created.Reader.ReadAsync(cancellationToken);
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime)
        {
            _callback = callback;
            _state = state;
            DueTime = dueTime;
        }

        public TimeSpan DueTime { get; }

        public void Fire() => _callback(_state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
            Guid workspaceId,
            string connectorId,
            IReadOnlyDictionary<string, string> settings,
            Guid? printerId,
            Guid? agentId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UploadFileAsync(Guid printerId, Stream stream, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
