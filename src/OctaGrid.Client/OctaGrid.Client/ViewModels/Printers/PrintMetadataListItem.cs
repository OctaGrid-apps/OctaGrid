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
}
