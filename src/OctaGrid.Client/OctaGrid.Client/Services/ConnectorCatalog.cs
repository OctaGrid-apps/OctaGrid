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
    /// <summary>How long <see cref="LoadAsync"/> waits before its first retry.</summary>
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>The longest <see cref="LoadAsync"/> waits between retries.</summary>
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

    private readonly IAgentCommandsService _commands;
    private readonly AgentDirectory _directory;
    private readonly ILogger<ConnectorCatalog> _logger;
    private readonly TimeProvider _time;
    private volatile ImmutableDictionary<Guid, IReadOnlyList<ConnectorDescriptor>> _byAgent =
        ImmutableDictionary<Guid, IReadOnlyList<ConnectorDescriptor>>.Empty;

    public ConnectorCatalog(
        IAgentCommandsService commands,
        AgentDirectory directory,
        ILogger<ConnectorCatalog>? logger = null,
        TimeProvider? time = null)
    {
        _commands = commands;
        _directory = directory;
        _logger = logger ?? NullLogger<ConnectorCatalog>.Instance;
        _time = time ?? TimeProvider.System;
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

    /// <summary>The connector with that id, or null when no agent offers it.</summary>
    public ConnectorDescriptor? Find(string connectorId) =>
        Connectors.FirstOrDefault(c => string.Equals(c.Id, connectorId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The connectors one agent offers, or none when it was not read yet.</summary>
    public IReadOnlyList<ConnectorDescriptor> ForAgent(Guid agentId) => _byAgent.GetValueOrDefault(agentId, []);

    /// <summary>
    /// Reads the connectors of every agent the user can see, and reads again with a growing delay until the agent
    /// list and every agent could be read, or until cancelled.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var delay = FirstRetryDelay;
        while (!await RefreshAsync(cancellationToken))
        {
            await Task.Delay(delay, _time, cancellationToken);
            delay = delay * 2 < MaxRetryDelay ? delay * 2 : MaxRetryDelay;
        }
    }

    /// <summary>
    /// Reads the connectors of every agent the user can see. An agent that cannot be reached keeps what was read
    /// from it before, so one offline agent does not hide the connectors of the others.
    /// </summary>
    /// <returns>True when the agent list and every agent could be read.</returns>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentChoice> agents;
        try
        {
            agents = await _directory.GetAgentsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not read the agents to load their connectors");
            return false;
        }

        var complete = true;
        foreach (var agent in agents)
        {
            complete &= await RefreshQuietlyAsync(agent.Id, cancellationToken, notify: false);
        }

        var known = agents.Select(a => a.Id).ToHashSet();
        _byAgent = _byAgent.RemoveRange(_byAgent.Keys.Where(id => !known.Contains(id)));
        Notify();
        return complete;
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

    private async Task<bool> RefreshQuietlyAsync(Guid agentId, CancellationToken cancellationToken = default, bool notify = true)
    {
        try
        {
            var connectors = await _commands.GetConnectorsAsync(agentId, cancellationToken);
            _byAgent = _byAgent.SetItem(agentId, connectors);
            if (notify)
            {
                Notify();
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not read the connectors of agent {AgentId}", agentId);
            return false;
        }
    }

    private static void Notify() => WeakReferenceMessenger.Default.Send(ConnectorCatalogChanged.Instance);
}
