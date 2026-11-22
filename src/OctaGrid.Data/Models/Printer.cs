using OctaGrid.Data.Events;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Data.Models;

/// <summary>
/// The root of the printer aggregate: its configuration, its encrypted secrets and its status. Refers to its workspace
/// and agent by id; both are aggregates of their own.
/// </summary>
public sealed class Printer : Entity
{
    // For EF.
    private Printer()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public Guid WorkspaceId { get; private set; }

    /// <summary>The agent that connects to the printer; null until one is assigned.</summary>
    public Guid? AgentId { get; private set; }

    public string? ReportedModel { get; private set; }
    public bool Enabled { get; private set; }
    public string ConnectorId { get; private set; } = string.Empty;
    public Dictionary<string, string> Settings { get; private set; } = [];

    /// <summary>
    /// The secrets, encrypted with the printer's id as the context, so they cannot be moved to another printer. The
    /// printer never sees them in plain text: a service encrypts and decrypts them.
    /// </summary>
    public string? ProtectedSecrets { get; private set; }

    public PrinterStatus? Status { get; private set; }

    /// <summary>
    /// Creates a printer without secrets, assigned to <paramref name="agent"/> when one is given.
    /// Its id is set here, not on save, because the secrets are encrypted for it before the first save.
    /// </summary>
    /// <exception cref="ValidationException">The agent is in another workspace.</exception>
    public static Printer Create(
        Guid workspaceId,
        string name,
        string connectorId,
        IReadOnlyDictionary<string, string> settings,
        bool enabled,
        Agent? agent = null)
    {
        var printer = new Printer
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            Name = name,
            ConnectorId = connectorId,
            Settings = new Dictionary<string, string>(settings),
            Enabled = enabled,
            Status = new PrinterStatus(),
        };

        if (agent is not null)
        {
            printer.EnsureSameWorkspace(agent);
            printer.AgentId = agent.Id;
        }

        printer.Raise(new PrinterAdded(printer.Id, workspaceId, printer.AgentId));
        return printer;
    }

    public void Rename(string name)
    {
        Name = name;
        RaiseChanged();
    }

    /// <summary>
    /// Changes the configuration. Saved secrets belong to the connector they were entered for, so another connector
    /// starts without them.
    /// </summary>
    public void Reconfigure(string name, string connectorId, IReadOnlyDictionary<string, string> settings, bool enabled)
    {
        if (!string.Equals(ConnectorId, connectorId, StringComparison.OrdinalIgnoreCase))
        {
            ProtectedSecrets = null;
        }

        Name = name;
        ConnectorId = connectorId;
        Settings = new Dictionary<string, string>(settings);
        Enabled = enabled;
        RaiseChanged();
    }

    /// <returns>Whether the state changed.</returns>
    public bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return false;
        }

        Enabled = enabled;
        RaiseChanged();
        return true;
    }

    /// <summary>Hands the printer to an agent of its own workspace.</summary>
    /// <exception cref="ValidationException">The agent is in another workspace.</exception>
    public void AssignTo(Agent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        EnsureSameWorkspace(agent);
        ChangeAgent(agent.Id);
    }

    /// <summary>Takes the printer away from its agent; it waits for another one.</summary>
    public void Unassign() => ChangeAgent(null);

    /// <summary>
    /// Records that the printer is being removed. Call before removing it from the context,
    /// so the removal is saved with its <see cref="PrinterRemoved"/> event.
    /// </summary>
    public void Remove() => Raise(new PrinterRemoved(Id, WorkspaceId, AgentId));

    /// <summary>
    /// Records a state reported by the printer's agent. A report older than the stored state is ignored,
    /// because reports can arrive out of order. Requires <see cref="Status"/> to be loaded.
    /// </summary>
    /// <returns>True when the state was recorded.</returns>
    public bool ObserveState(UniversalPrinterState state, DateTimeOffset observedAt)
    {
        Status ??= new PrinterStatus();
        return Status.Observe(state, observedAt);
    }

    /// <summary>Stores the encrypted secrets, or none.</summary>
    public void StoreSecrets(string? protectedSecrets)
    {
        ProtectedSecrets = string.IsNullOrEmpty(protectedSecrets) ? null : protectedSecrets;

        // A new printer reports its secrets with PrinterAdded.
        if (!DomainEvents.OfType<PrinterAdded>().Any())
        {
            RaiseChanged();
        }
    }

    private void EnsureSameWorkspace(Agent agent)
    {
        if (agent.WorkspaceId != WorkspaceId)
        {
            throw new ValidationException(nameof(AgentId), "The agent is in another workspace.");
        }
    }

    private void ChangeAgent(Guid? agentId)
    {
        if (AgentId == agentId)
        {
            return;
        }

        var previous = AgentId;
        AgentId = agentId;
        Raise(new PrinterAgentChanged(Id, WorkspaceId, agentId, previous));
    }

    // One PrinterChanged per save is enough: it names the printer, not what changed.
    private void RaiseChanged()
    {
        if (!DomainEvents.OfType<PrinterChanged>().Any())
        {
            Raise(new PrinterChanged(Id, WorkspaceId));
        }
    }
}
