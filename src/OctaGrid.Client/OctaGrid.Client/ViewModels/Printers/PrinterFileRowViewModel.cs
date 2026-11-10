using CommunityToolkit.Mvvm.Input;

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
        Func<bool> canPrint)
    {
        File = file;
        PrintCommand = new AsyncRelayCommand(ct => print(file, ct), canPrint);
        DeleteCommand = new AsyncRelayCommand(ct => delete(file, ct));
    }

    /// <summary>The file, with its metadata and thumbnail (the thumbnail arrives after the row is shown).</summary>
    public PrintMetadataListItem File { get; }

    /// <summary>Starts printing the file. Not available while the printer is busy or cannot be reached.</summary>
    public IAsyncRelayCommand PrintCommand { get; }

    /// <summary>Deletes the file from the printer, after asking.</summary>
    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>Material and nozzle under the file name, for example "PLA • 0.4 mm".</summary>
    public string DetailsText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(File.Metadata.MaterialType))
            {
                parts.Add(File.Metadata.MaterialType);
            }

            if (File.Metadata.NozzleDiameter is { } nozzle)
            {
                parts.Add($"{nozzle.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)} mm");
            }

            return string.Join(" • ", parts);
        }
    }
}
