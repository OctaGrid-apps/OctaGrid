namespace OctaGrid.Data.Models;

/// <summary>How a print job ended. Stored by name, so the order here is free.</summary>
public enum PrintJobOutcome
{
    /// <summary>The print finished.</summary>
    Completed,

    /// <summary>The print ended with an error, or the printer faulted during it.</summary>
    Failed,

    /// <summary>The print was cancelled.</summary>
    Cancelled,

    /// <summary>
    /// The job is over but how it ended was not seen: the printer went idle, or started another file, without
    /// reporting an end state first.
    /// </summary>
    Unknown,
}
