using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.ViewModels;

public record UploadFileRequest(Stream Stream, string FileName);

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

public partial class FilePickerViewModel : ViewModelBase, IDialogParticipant<PrintMetadata>
{
    private readonly IAgentCommandsService _agentCommandsService;
    private readonly Guid _deviceId;
    private readonly IByteArrayImageCache _imageCache;

    public FilePickerViewModel(Guid deviceId)
    {
        _agentCommandsService = App.Services.GetRequiredService<IAgentCommandsService>();
        _imageCache = App.Services.GetRequiredService<IByteArrayImageCache>();
        _deviceId = deviceId;
    }

    [ObservableProperty]
    public partial ObservableCollection<PrintMetadataListItem> Files { get; set; } = [];

    [ObservableProperty]
    public partial bool IsFilesLoading { get; set; }

    [ObservableProperty]
    public partial PrintMetadataListItem? SelectedItem { get; set; }

    [RelayCommand]
    private void Cancel()
    {
        this.ReturnResultFromOverlayDialog(null);
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedItem is null)
        {
            return;
        }
        this.ReturnResultFromOverlayDialog(SelectedItem.Metadata);
    }

    [RelayCommand]
    private async Task DeleteFile(CancellationToken cancellationToken)
    {
        if (SelectedItem is null)
        {
            return;
        }
        //ToDo: show confirmation dialog
        await _agentCommandsService.DeleteFileAsync(_deviceId, SelectedItem.Metadata.FileName, cancellationToken);
        await LoadData(cancellationToken);
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        try
        {
            IsFilesLoading = true;
            var files = await _agentCommandsService.GetPrinterFilesAsync(_deviceId, cancellationToken);
            Files = new ObservableCollection<PrintMetadataListItem>(files.Select(x => new PrintMetadataListItem(x, [])));

            foreach (var file in Files)
            {
                await TryGetThumbnail(file);
            }
        }
        finally
        {
            IsFilesLoading = false;
        }
    }

    private async Task TryGetThumbnail(PrintMetadataListItem printMetadataListItem)
    {
        if (string.IsNullOrWhiteSpace(printMetadataListItem.Metadata.FileName))
        {
            return;
        }

        if (_imageCache.TryGetBytes(printMetadataListItem.Metadata.FileName, out var bytes) && bytes is not null)
        {
            printMetadataListItem.ThumbnailData = bytes;
        }
        else
        {
            var response = await _agentCommandsService.GetThumbnailAsync(_deviceId, printMetadataListItem.Metadata.FileName, CancellationToken.None);
            if (response is not null)
            {
                _imageCache.Add(printMetadataListItem.Metadata.FileName, response.Data);
                printMetadataListItem.ThumbnailData = response.Data;
            }
        }
    }

    [RelayCommand]
    private async Task UploadFile(UploadFileRequest request, CancellationToken cancellationToken)
    {
        await _agentCommandsService.UploadFileAsync(_deviceId, request.Stream, request.FileName, cancellationToken);
        await LoadData(cancellationToken);
    }
}
