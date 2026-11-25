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
    // Thumbnails asked for at once. The agent reads at most three files from a printer at a time anyway.
    private const int ThumbnailConcurrency = 3;

    private readonly IAgentCommandsService _agentCommandsService;
    private CancellationTokenSource? _thumbnails;
    private readonly Guid _printerId;
    private readonly IByteArrayImageCache _imageCache;
    private readonly ILogger<FilePickerViewModel> _logger;

    public FilePickerViewModel(
        Guid printerId,
        IAgentCommandsService agentCommands,
        IByteArrayImageCache imageCache,
        ILogger<FilePickerViewModel>? logger = null,
        WorkspacePermissions? permissions = null)
    {
        _agentCommandsService = agentCommands;
        _imageCache = imageCache;
        _logger = logger ?? NullLogger<FilePickerViewModel>.Instance;
        _printerId = printerId;
        Permissions = permissions ?? WorkspacePermissions.Unrestricted;
    }

    /// <summary>What the user may do with the files. Buttons for anything else are not shown.</summary>
    public WorkspacePermissions Permissions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial ObservableCollection<PrintMetadataListItem> Files { get; set; } = [];

    /// <summary>The orders the files can be listed in.</summary>
    public IReadOnlyList<FileSortOption> SortOptions => FileSortOption.All;

    /// <summary>How the files are ordered; newest first by default, as the printer's own screen lists them.</summary>
    [ObservableProperty]
    public partial FileSortOption SelectedSort { get; set; } = FileSortOption.All[0];

    // Re-ordering keeps the items, so thumbnails already shown stay and those still loading land where they belong.
    partial void OnSelectedSortChanged(FileSortOption value)
    {
        if (value is not null)
        {
            Files = new ObservableCollection<PrintMetadataListItem>(FileSortOption.Sort(Files, f => f.Metadata, value.Order));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// The load failure with its Try again button. Hidden while the printer is offline: asking again cannot succeed then,
    /// and <see cref="IsOffline" /> already says why there are no files.
    /// </summary>
    public bool IsErrorVisible => HasError && !IsOffline;

    /// <summary>The printer answered and has no files; never true while loading, after a failure or while offline.</summary>
    public bool IsEmpty => !IsFilesLoading && !HasError && !IsOffline && Files.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsFilesLoading { get; set; }

    /// <summary>
    /// The printer is not connected, so its files cannot be asked for. Set by whoever follows the printer's state; while
    /// it is true the list cannot be loaded.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsErrorVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(LoadDataCommand))]
    public partial bool IsOffline { get; set; }

    [ObservableProperty]
    public partial PrintMetadataListItem? SelectedItem { get; set; }

    [RelayCommand]
    private void Cancel()
    {
        _logger.LogDebug("FilePicker dialog cancelled for printer {PrinterId}", _printerId);
        StopLoadingThumbnails();
        this.ReturnResultFromOverlayDialog(null);
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedItem is null)
        {
            return;
        }
        _logger.LogDebug("FilePicker dialog selected file '{FileName}' for printer {PrinterId}", SelectedItem.Metadata.FileName, _printerId);
        StopLoadingThumbnails();
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

            _logger.LogInformation("Deleting file '{FileName}' on printer {PrinterId}", fileName, _printerId);
            await _agentCommandsService.DeleteFileAsync(_printerId, fileName, cancellationToken);
            await LoadData(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to delete file '{FileName}' on printer {PrinterId}", fileName, _printerId);
            throw;
        }
    }

    private bool CanLoadData() => !IsOffline;

    // A failure is shown in the list (ErrorMessage) and not rethrown: this runs from buttons (Refresh, Try again), and an
    // exception escaping a button's command ends the app.
    [RelayCommand(CanExecute = nameof(CanLoadData))]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        StopLoadingThumbnails();
        try
        {
            IsFilesLoading = true;
            ErrorMessage = null;
            _logger.LogDebug("Loading printer files for printer {PrinterId}", _printerId);
            var files = await _agentCommandsService.GetPrinterFilesAsync(_printerId, cancellationToken);
            Files = new ObservableCollection<PrintMetadataListItem>(
                FileSortOption.Sort(files.Select(x => new PrintMetadataListItem(x, [])), f => f.Metadata, SelectedSort.Order));
            _logger.LogInformation("Loaded {FileCount} file(s) from printer {PrinterId}", Files.Count, _printerId);

            // The list is shown now; pictures fill in as they arrive instead of holding it back.
            _thumbnails = new CancellationTokenSource();
            ThumbnailsLoading = LoadThumbnailsAsync(Files.ToList(), _thumbnails.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Loading printer files for printer {PrinterId} was cancelled", _printerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load printer files for printer {PrinterId}", _printerId);
            ErrorMessage = ex is AppException ? ex.Message : "The files could not be loaded.";
        }
        finally
        {
            IsFilesLoading = false;
        }
    }

    /// <summary>The thumbnails of the last load, finished when every one arrived, failed or was stopped.</summary>
    internal Task ThumbnailsLoading { get; private set; } = Task.CompletedTask;

    /// <summary>Stops asking for thumbnails, when the list is closed or loaded again.</summary>
    public void StopLoadingThumbnails()
    {
        _thumbnails?.Cancel();
        _thumbnails?.Dispose();
        _thumbnails = null;
    }

    // Started from the UI thread, so each picture is set there when its request returns; the gate only limits how many
    // requests are open at once.
    private async Task LoadThumbnailsAsync(IReadOnlyList<PrintMetadataListItem> items, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(ThumbnailConcurrency);
        await Task.WhenAll(items.Select(async item =>
        {
            try
            {
                await gate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                // Stopping ends the open requests, and one of them can hand its slot to a waiter before the waiter
                // sees the cancellation; that waiter must not ask after all.
                if (!cancellationToken.IsCancellationRequested)
                {
                    await TryGetThumbnail(item, cancellationToken);
                }
            }
            finally
            {
                gate.Release();
            }
        }));
    }

    private async Task TryGetThumbnail(PrintMetadataListItem item, CancellationToken cancellationToken)
    {
        var file = item.Metadata;
        if (string.IsNullOrWhiteSpace(file.FileName))
        {
            return;
        }

        // The cache is shared by every printer, and a file can be replaced by another of the same name.
        var key = $"{_printerId}/{file.FileName}/{file.FileSize}/{file.ModifiedAt?.UtcTicks}";
        try
        {
            if (_imageCache.TryGetBytes(key, out var bytes) && bytes is not null)
            {
                item.ThumbnailData = bytes;
                return;
            }

            var response = await _agentCommandsService.GetThumbnailAsync(_printerId, file.FileName, cancellationToken);
            if (response is not null)
            {
                _imageCache.Add(key, response.Data);
                item.ThumbnailData = response.Data;
            }
        }
        catch (OperationCanceledException)
        {
            // The list was closed or loaded again; this picture is no longer wanted.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load thumbnail for file '{FileName}' on printer {PrinterId}", file.FileName, _printerId);
        }
    }

    [RelayCommand]
    private async Task UploadFile(UploadFileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Uploading file '{FileName}' to printer {PrinterId}", request.FileName, _printerId);
            await _agentCommandsService.UploadFileAsync(_printerId, request.Stream, request.FileName, cancellationToken);
            await LoadData(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to upload file '{FileName}' to printer {PrinterId}", request.FileName, _printerId);
            throw;
        }
    }
}
