using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Client.ViewModels;

public partial class FilePickerViewModel : ViewModelBase, IDialogParticipant<PrintMetadata>
{
    private readonly IAgentCommandsService _agentCommandsService;
    private readonly Guid _deviceId;
    private readonly IByteArrayImageCache _imageCache;
    private readonly ILogger<FilePickerViewModel> _logger;

    public FilePickerViewModel(
        Guid deviceId,
        IAgentCommandsService agentCommands,
        IByteArrayImageCache imageCache,
        ILogger<FilePickerViewModel>? logger = null)
    {
        _agentCommandsService = agentCommands;
        _imageCache = imageCache;
        _logger = logger ?? NullLogger<FilePickerViewModel>.Instance;
        _deviceId = deviceId;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial ObservableCollection<PrintMetadataListItem> Files { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>The printer answered and has no files; never true while loading or after a failure.</summary>
    public bool IsEmpty => !IsFilesLoading && !HasError && Files.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsFilesLoading { get; set; }

    [ObservableProperty]
    public partial PrintMetadataListItem? SelectedItem { get; set; }

    [RelayCommand]
    private void Cancel()
    {
        _logger.LogDebug("FilePicker dialog cancelled for device {DeviceId}", _deviceId);
        this.ReturnResultFromOverlayDialog(null);
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedItem is null)
        {
            return;
        }
        _logger.LogDebug("FilePicker dialog selected file '{FileName}' for device {DeviceId}", SelectedItem.Metadata.FileName, _deviceId);
        this.ReturnResultFromOverlayDialog(SelectedItem.Metadata);
    }

    [RelayCommand]
    private Task DeleteFile(CancellationToken cancellationToken) =>
        SelectedItem is { } selected ? DeleteItemAsync(selected, cancellationToken) : Task.CompletedTask;

    /// <summary>Deletes one file from the printer after asking, then shows the files that are left.</summary>
    internal async Task DeleteItemAsync(PrintMetadataListItem item, CancellationToken cancellationToken)
    {
        var fileName = item.Metadata.FileName;
        try
        {
            var confirm = new ConfirmViewModel($"Delete \"{fileName}\" from the printer? This cannot be undone.", "Delete");
            if (await this.ShowOverlayDialogAsync("Delete file", confirm) != true)
            {
                return;
            }

            _logger.LogInformation("Deleting file '{FileName}' on device {DeviceId}", fileName, _deviceId);
            await _agentCommandsService.DeleteFileAsync(_deviceId, fileName, cancellationToken);
            await LoadData(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to delete file '{FileName}' on device {DeviceId}", fileName, _deviceId);
            throw;
        }
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        try
        {
            IsFilesLoading = true;
            ErrorMessage = null;
            _logger.LogDebug("Loading printer files for device {DeviceId}", _deviceId);
            var files = await _agentCommandsService.GetPrinterFilesAsync(_deviceId, cancellationToken);
            Files = new ObservableCollection<PrintMetadataListItem>(files.Select(x => new PrintMetadataListItem(x, [])));
            _logger.LogInformation("Loaded {FileCount} file(s) from device {DeviceId}", Files.Count, _deviceId);

            foreach (var file in Files)
            {
                await TryGetThumbnail(file);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Loading printer files for device {DeviceId} was cancelled", _deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load printer files for device {DeviceId}", _deviceId);
            ErrorMessage = ex is AppException ? ex.Message : "The files could not be loaded.";
            throw;
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

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load thumbnail for file '{FileName}' on device {DeviceId}", printMetadataListItem.Metadata.FileName, _deviceId);
        }
    }

    [RelayCommand]
    private async Task UploadFile(UploadFileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Uploading file '{FileName}' to device {DeviceId}", request.FileName, _deviceId);
            await _agentCommandsService.UploadFileAsync(_deviceId, request.Stream, request.FileName, cancellationToken);
            await LoadData(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to upload file '{FileName}' to device {DeviceId}", request.FileName, _deviceId);
            throw;
        }
    }
}
