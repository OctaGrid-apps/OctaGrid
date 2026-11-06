using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Extensions.Abstractions;

/// <summary>Optional capability: control print jobs. Declare <see cref="ConnectorCapabilities.PrintControl"/>.</summary>
public interface IPrintControl
{
    /// <summary>Starts printing a file that is already stored on the printer.</summary>
    Task StartPrintAsync(string fileName, CancellationToken cancellationToken);

    /// <summary>Pauses the current print.</summary>
    Task PausePrintAsync(CancellationToken cancellationToken);

    /// <summary>Resumes a paused print.</summary>
    Task ResumePrintAsync(CancellationToken cancellationToken);

    /// <summary>Cancels the current print.</summary>
    Task StopPrintAsync(CancellationToken cancellationToken);
}

/// <summary>Optional capability: manage files on the printer. Declare <see cref="ConnectorCapabilities.Files"/>.</summary>
public interface IFileStorage
{
    /// <summary>Lists the printable files stored on the printer.</summary>
    Task<IReadOnlyList<PrintMetadata>> ListFilesAsync(CancellationToken cancellationToken);

    /// <summary>Uploads a file to the printer.</summary>
    Task UploadFileAsync(Stream content, string fileName, CancellationToken cancellationToken);

    /// <summary>Deletes a file from the printer.</summary>
    Task DeleteFileAsync(string fileName, CancellationToken cancellationToken);
}

/// <summary>Optional capability: thumbnails. Declare <see cref="ConnectorCapabilities.Thumbnails"/>.</summary>
public interface IThumbnails
{
    /// <summary>Returns null when the file has no thumbnail or it could not be fetched.</summary>
    Task<PrintThumbnail?> GetThumbnailAsync(string fileName, CancellationToken cancellationToken);
}

/// <summary>
/// Optional capability: custom commands for Expert mode. Declare
/// <see cref="ConnectorCapabilities.CustomCommands"/> and list the commands in
/// <see cref="ConnectorDescriptor.CustomCommands"/>. The host only runs declared commands.
/// </summary>
public interface ICustomCommands
{
    /// <param name="commandId">Id of a command declared in the descriptor.</param>
    /// <param name="arguments">Values for the command's parameters, by <see cref="SettingField.Key"/>.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    Task<CommandResult> ExecuteAsync(
        string commandId,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional capability of the <see cref="IConnector"/> itself (not of a session): find printers on the
/// network. Declare <see cref="ConnectorCapabilities.Discovery"/>.
/// </summary>
public interface IPrinterDiscovery
{
    /// <summary>Scans the network and yields printers as they are found, until the timeout or cancellation.</summary>
    IAsyncEnumerable<DiscoveredPrinter> ScanAsync(DiscoveryOptions options, CancellationToken cancellationToken);
}

/// <summary>Options for a network scan.</summary>
/// <param name="Timeout">How long the scan may run.</param>
/// <param name="NetworkInterfaces">Interfaces to scan; empty means all private networks.</param>
public sealed record DiscoveryOptions(TimeSpan Timeout, IReadOnlyList<string> NetworkInterfaces);
