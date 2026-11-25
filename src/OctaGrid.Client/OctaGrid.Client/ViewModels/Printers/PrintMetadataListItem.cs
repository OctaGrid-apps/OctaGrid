using CommunityToolkit.Mvvm.ComponentModel;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.ViewModels;

public partial class PrintMetadataListItem : ObservableObject
{
    public PrintMetadataListItem(PrintMetadata metadata, byte[] thumbnailData)
    {
        Metadata = metadata;
        ThumbnailData = thumbnailData;
    }

    public PrintMetadata Metadata { get; }

    [ObservableProperty]
    public partial byte[] ThumbnailData { get; set; }

    /// <summary>Material, nozzle and filament weight, for example "PLA • 0.4 mm • 12.5 g".</summary>
    public string DetailsText => PrintFileText.Details(Metadata);

    /// <summary>Details, time, size and date on one line, for the compact file picker.</summary>
    public string SummaryText => PrintFileText.Summary(Metadata);

    /// <summary>The file's size, for example "4.2 MB".</summary>
    public string SizeText => PrintFileText.Size(Metadata.FileSize);

    /// <summary>The day the file was written, or null when the printer does not say.</summary>
    public string? DateText => PrintFileText.Date(Metadata.ModifiedAt);
}
