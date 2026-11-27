using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Client.ViewModels;

public partial class FilePickerViewModel : ViewModelBase, IDialogParticipant<PrintMetadata>
{
    // Matches the agent's limit of three FTPS connections per printer.
    private const int ThumbnailConcurrency = 3;

    private readonly IAgentCommandsService _agentCommandsService;
    // Files whose details are asked for at once: about a screenful, and few enough to come back quickly.
    private const int DetailsBatchSize = 8;

    private CancellationTokenSource? _thumbnails;
    private readonly Guid _printerId;
    private readonly IByteArrayImageCache _imageCache;
    private readonly ILogger<FilePickerViewModel> _logger;
    private readonly IAppSettingsStore? _settings;

    /// <param name="settings">Where the chosen order is kept, so every file list opens in it. Without it, newest first.</param>
    public FilePickerViewModel(
        Guid printerId,
        IAgentCommandsService agentCommands,
        IByteArrayImageCache imageCache,
        ILogger<FilePickerViewModel>? logger = null,
        WorkspacePermissions? permissions = null,
        IAppSettingsStore? settings = null)
    {
        _agentCommandsService = agentCommands;
        _imageCache = imageCache;
        _logger = logger ?? NullLogger<FilePickerViewModel>.Instance;
        _printerId = printerId;
        Permissions = permissions ?? WorkspacePermissions.Unrestricted;
        SelectedSort = FileSortOption.Of(settings?.ClientSettings.FileSortOrder);

        // Set after the saved order is applied, so opening a list does not write it back.
        _settings = settings;
    }

    /// <summary>What the user may do with the files. Buttons for anything else are not shown.</summary>
    public WorkspacePermissions Permissions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial ObservableCollection<PrintMetadataListItem> Files { get; set; } = [];

    public IReadOnlyList<FileSortOption> SortOptions => FileSortOption.All;

    /// <summary>
    /// The file order: the one last chosen in any file list, or newest first, as on the printer's screen.
    /// </summary>
    [ObservableProperty]
    public partial FileSortOption SelectedSort { get; set; } = FileSortOption.All[0];

    // Reuses the items, so loaded and pending thumbnails stay with their files.
    partial void OnSelectedSortChanged(FileSortOption value)
    {
        if (value is null)
        {
            return;
        }

        Files = new ObservableCollection<PrintMetadataListItem>(FileSortOption.Sort(Files, f => f.Metadata, value.Order));
        if (_settings is not null && _settings.ClientSettings.FileSortOrder != value.Order)
        {
            _settings.Update(s => s with { FileSortOrder = value.Order });
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

            // Details and thumbnails fill in after the list is shown. An overlapping load may have started its own.
            StopLoadingThumbnails();
            _thumbnails = new CancellationTokenSource();
            ThumbnailsLoading = FillInAsync(_thumbnails.Token);
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

    /// <summary>Completes when every detail and thumbnail of the last load arrived, failed or was stopped.</summary>
    internal Task ThumbnailsLoading { get; private set; } = Task.CompletedTask;

    // Walks the files in the order they are shown, a batch at a time: first the details the listing left out, then
    // the thumbnails. The top of the list fills first, and a new sort order applies from the next batch on.
    private async Task FillInAsync(CancellationToken cancellationToken)
    {
        var done = new HashSet<PrintMetadataListItem>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = Files.Where(f => !done.Contains(f)).Take(DetailsBatchSize).ToList();
            if (batch.Count == 0)
            {
                return;
            }

            done.UnionWith(batch);
            var pending = batch.Where(f => f.Metadata.DetailsPending).ToList();
            if (pending.Count > 0)
            {
                await FillDetailsAsync(pending, cancellationToken);
            }

            await LoadThumbnailsAsync(batch, cancellationToken);
        }
    }

    private async Task FillDetailsAsync(IReadOnlyList<PrintMetadataListItem> items, CancellationToken cancellationToken)
    {
        IReadOnlyList<PrintMetadata> details;
        try
        {
            details = await _agentCommandsService.GetPrinterFileDetailsAsync(
                _printerId, items.Select(i => i.Metadata.FileName).ToList(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read file details on printer {PrinterId}", _printerId);
            details = [];
        }

        // A file the printer did not describe keeps what the listing said, rather than "reading" for ever.
        var byName = details
            .GroupBy(d => d.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            item.Metadata = byName.TryGetValue(item.Metadata.FileName, out var read)
                ? read with { DetailsPending = false }
                : item.Metadata with { DetailsPending = false };
        }
    }

    /// <summary>Cancels the thumbnail requests of the last load.</summary>
    public void StopLoadingThumbnails()
    {
        _thumbnails?.Cancel();
        _thumbnails?.Dispose();
        _thumbnails = null;
    }

    // Runs on the UI thread, so each thumbnail is set there; the gate only limits open requests.
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
                // A cancelled request can pass its slot to a waiter before the waiter sees the cancellation.
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

        // The cache is shared by all printers, and a file can be replaced under the same name.
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
            // Stopped by StopLoadingThumbnails.
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
