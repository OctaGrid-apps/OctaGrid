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

    /// <summary>The file; replaced when its details arrive after the listing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText), nameof(SummaryText), nameof(SizeText), nameof(DateText))]
    public partial PrintMetadata Metadata { get; set; }

    [ObservableProperty]
    public partial byte[] ThumbnailData { get; set; }

    public string DetailsText => Metadata.DetailsPending ? PrintFileText.ReadingDetails : PrintFileText.Details(Metadata);

    public string SummaryText => Metadata.DetailsPending
        ? $"{PrintFileText.ReadingDetails} • {PrintFileText.Size(Metadata.FileSize)}"
        : PrintFileText.Summary(Metadata);

    public string SizeText => PrintFileText.Size(Metadata.FileSize);

    public string? DateText => PrintFileText.Date(Metadata.ModifiedAt);
}
