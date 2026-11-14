namespace OctaGrid.Client.Local;

/// <summary>
/// The agent that runs inside the client in Local mode. It is an ordinary <c>Agent</c> row, provisioned at
/// startup (see <see cref="DependencyInjection.ApplyMigrations"/>), and printers are assigned to it like to any
/// other agent; this only remembers which row it is.
/// </summary>
internal sealed class LocalAgent
{
    private Guid? _id;

    /// <summary>The id of the local agent's row.</summary>
    /// <exception cref="InvalidOperationException">Startup did not provision the local agent.</exception>
    public Guid Id => _id ?? throw new InvalidOperationException("The local agent has not been provisioned; the database could not be prepared at startup.");

    /// <summary>The id, or null before startup provisioned it.</summary>
    public Guid? KnownId => _id;

    public void Set(Guid id) => _id = id;
}
