namespace OctaGrid.Client.Local;

/// <summary>The agent that runs inside the client in Local mode.</summary>
internal static class LocalAgent
{
    /// <summary>
    /// The id requests to the in-process agent are addressed to. It is a fixed value for now: agents are
    /// stored per workspace, and a real row for the local agent comes together with printers getting an
    /// owning agent. Until then nothing persists this id.
    /// </summary>
    public static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-00000000a9e1");
}
