using System.Collections.Immutable;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.Services;

/// <summary>Sent after the connectors the client knows changed, on the thread that changed them.</summary>
public sealed class ConnectorCatalogChanged
{
    public static readonly ConnectorCatalogChanged Instance = new();

    private ConnectorCatalogChanged()
    {
    }
}

/// <summary>
/// The connectors of every agent the user can see, kept so views can name a printer's connector and check its
/// capabilities without waiting. Each agent has its own set (extensions are installed per agent), so they are read
/// per agent and read again when an agent reports that its set changed.
/// <para>
/// Use it from the UI thread. Its refreshes start there (at startup, and from the message the client's message
/// handler posts there), so they finish there and <see cref="ConnectorCatalogChanged"/> reaches view models on it.
/// </para>
/// </summary>
public sealed class ConnectorCatalog
{
    private readonly IAgentCommandsService _commands;
    private readonly AgentDirectory _directory;
    private readonly ILogger<ConnectorCatalog> _logger;
    private volatile ImmutableDictionary<Guid, IReadOnlyList<ConnectorDescriptor>> _byAgent =
        ImmutableDictionary<Guid, IReadOnlyList<ConnectorDescriptor>>.Empty;

    public ConnectorCatalog(IAgentCommandsService commands, AgentDirectory directory, ILogger<ConnectorCatalog>? logger = null)
    {
        _commands = commands;
        _directory = directory;
        _logger = logger ?? NullLogger<ConnectorCatalog>.Instance;
        WeakReferenceMessenger.Default.Register<AgentConnectorsChangedMessage>(this, (_, message) => _ = RefreshQuietlyAsync(message.AgentId));
    }

    /// <summary>
    /// Every known connector once, by id; the first agent that offers it describes it. Empty until the first
    /// <see cref="RefreshAsync(CancellationToken)"/> finished.
    /// </summary>
    public IReadOnlyList<ConnectorDescriptor> Connectors =>
        _byAgent.OrderBy(pair => pair.Key)
            .SelectMany(pair => pair.Value)
            .DistinctBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The agent when the user can see exactly one, as in Local mode, or null. A new printer goes to that agent,
    /// so it is the one that tests the new printer's connection.
    /// </summary>
    public Guid? SoleAgentId => _byAgent.Count == 1 ? _byAgent.Keys.First() : null;

    /// <summary>The connector with that id, or null when no agent offers it.</summary>
    public ConnectorDescriptor? Find(string connectorId) =>
        Connectors.FirstOrDefault(c => string.Equals(c.Id, connectorId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The connectors one agent offers, or none when it was not read yet.</summary>
    public IReadOnlyList<ConnectorDescriptor> ForAgent(Guid agentId) => _byAgent.GetValueOrDefault(agentId, []);

    /// <summary>
    /// Reads the connectors of every agent the user can see. An agent that cannot be reached keeps what was read
    /// from it before, so one offline agent does not hide the connectors of the others.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var agents = await _directory.GetAgentsAsync(cancellationToken);
        var known = agents.Select(a => a.Id).ToHashSet();
        foreach (var agent in agents)
        {
            await RefreshQuietlyAsync(agent.Id, cancellationToken, notify: false);
        }

        _byAgent = _byAgent.RemoveRange(_byAgent.Keys.Where(id => !known.Contains(id)));
        Notify();
    }

    /// <summary>Reads the connectors of one agent again.</summary>
    public async Task RefreshAsync(Guid agentId, CancellationToken cancellationToken)
    {
        Set(agentId, await _commands.GetConnectorsAsync(agentId, cancellationToken));
    }

    /// <summary>Replaces what is known about one agent's connectors.</summary>
    public void Set(Guid agentId, IReadOnlyList<ConnectorDescriptor> connectors)
    {
        ArgumentNullException.ThrowIfNull(connectors);
        _byAgent = _byAgent.SetItem(agentId, connectors);
        Notify();
    }

    private async Task RefreshQuietlyAsync(Guid agentId, CancellationToken cancellationToken = default, bool notify = true)
    {
        try
        {
            var connectors = await _commands.GetConnectorsAsync(agentId, cancellationToken);
            _byAgent = _byAgent.SetItem(agentId, connectors);
            if (notify)
            {
                Notify();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not read the connectors of agent {AgentId}", agentId);
        }
    }

    private static void Notify() => WeakReferenceMessenger.Default.Send(ConnectorCatalogChanged.Instance);
}
