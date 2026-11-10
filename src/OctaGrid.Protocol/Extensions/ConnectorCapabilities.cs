namespace OctaGrid.Protocol.Extensions;

/// <summary>
/// What a connector can do. Declared in the <see cref="ConnectorDescriptor"/> so a client can enable
/// or hide features without loading any connector code; the host verifies that the declaration
/// matches the interfaces the connector actually implements.
/// </summary>
[Flags]
public enum ConnectorCapabilities
{
    None = 0,

    /// <summary>Start, pause, resume and stop print jobs.</summary>
    PrintControl = 1,

    /// <summary>List, upload and delete files stored on the printer.</summary>
    Files = 2,

    /// <summary>Fetch a thumbnail for a file or the current print job.</summary>
    Thumbnails = 4,

    /// <summary>Expose custom commands (Expert mode).</summary>
    CustomCommands = 8,

    /// <summary>Find printers on the network.</summary>
    Discovery = 16,

    /// <summary>
    /// Report what the printer writes to its console (G-code responses, messages) so the user can read it. Read-only:
    /// sending commands to the printer is a separate capability, because it can do harm.
    /// </summary>
    Console = 32,
}
