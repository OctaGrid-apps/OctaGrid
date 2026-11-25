using CommunityToolkit.Mvvm.Input;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

/// <summary>
/// One file on the printer page's Files tab: the file and what can be done to it, so every row carries its own Print and
/// Delete buttons. The page makes the rows and decides what the commands do.
/// </summary>
public sealed class PrinterFileRowViewModel
{
    public PrinterFileRowViewModel(
        PrintMetadataListItem file,
        Func<PrintMetadataListItem, CancellationToken, Task> print,
        Func<PrintMetadataListItem, CancellationToken, Task> delete,
        Func<bool> canPrint,
        WorkspacePermissions? permissions = null)
    {
        File = file;
        Permissions = permissions ?? WorkspacePermissions.Unrestricted;
        PrintCommand = new AsyncRelayCommand(ct => print(file, ct), canPrint);
        DeleteCommand = new AsyncRelayCommand(ct => delete(file, ct));
    }

    /// <summary>The file, with its metadata and thumbnail (the thumbnail arrives after the row is shown).</summary>
    public PrintMetadataListItem File { get; }

    /// <summary>What the user may do with the file. Buttons for anything else are not shown.</summary>
    public WorkspacePermissions Permissions { get; }

    /// <summary>Starts printing the file. Not available while the printer is busy or cannot be reached.</summary>
    public IAsyncRelayCommand PrintCommand { get; }

    /// <summary>Deletes the file from the printer, after asking.</summary>
    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>Material, nozzle and filament weight under the file name, for example "PLA • 0.4 mm • 12.5 g".</summary>
    public string DetailsText => File.DetailsText;
}
