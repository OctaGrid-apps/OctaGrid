using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.ViewModels;

public partial class PrinterDashboardCardViewModel : ViewModelBase, IDialogParticipant
{
    private readonly IAgentCommandsService _agentCommands;
    private readonly IByteArrayImageCache _imageCache;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PrinterDashboardCardViewModel> _logger;

    // How long a just-started file name is kept while the printer still reports an idle state.
    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider;
    private string? _startedFileName;
    private string? _previousFileName;
    private DateTimeOffset _startedAt;

    public PrinterDashboardCardViewModel(
        PrinterStatusWidget item,
        IAgentCommandsService agentCommands,
        IByteArrayImageCache imageCache,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null,
        TemperatureHistory? temperatureHistory = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        TemperatureHistory = temperatureHistory ?? new TemperatureHistory(_timeProvider);
        Item = item;
        _agentCommands = agentCommands;
        _imageCache = imageCache;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<PrinterDashboardCardViewModel>();

        WeakReferenceMessenger.Default.Register<PrinterTelemetryData, Guid>(this, item.Id, HandlePrinterTelemetry);
        _ = LoadData(CancellationToken.None);
    }

    [ObservableProperty]
    public partial PrinterStatusWidget Item { get; private set; }

    /// <summary>
    /// The recent temperatures of this printer. They are collected from telemetry by the app and handed to the card, because
    /// the card itself is created again whenever the dashboard is shown again.
    /// </summary>
    public TemperatureHistory TemperatureHistory { get; }

    private void HandlePrinterTelemetry(object recipient, PrinterTelemetryData message)
    {
        if (message.Value.NozzleTemperature.HasValue)
        {
            Item.NozzleTemperature = message.Value.NozzleTemperature.Value;
        }

        if (message.Value.NozzleTargetTemperature.HasValue)
        {
            Item.NozzleTargetTemperature = message.Value.NozzleTargetTemperature.Value;
        }

        if (message.Value.BedTemperature.HasValue)
        {
            Item.BedTemperature = message.Value.BedTemperature.Value;
        }

        if (message.Value.BedTargetTemperature.HasValue)
        {
            Item.BedTargetTemperature = message.Value.BedTargetTemperature.Value;
        }

        ResolveStartedFileName(message.Value.State);

        // While a just-started file is unconfirmed, an idle printer's file name is the previous job's.
        if (message.Value.PrintStatus.FileName is not null && _startedFileName is null)
        {
            Item.PrintFileName = message.Value.PrintStatus.FileName;
            _ = TryGetThumbnail();
        }

        if (message.Value.PrintStatus.Progress.HasValue)
        {
            Item.Progress = message.Value.PrintStatus.Progress.Value;
        }

        Item.TotalLayerCount = message.Value.PrintStatus.LayersCount;
        Item.CurrentLayer = message.Value.PrintStatus.CurrentLayer;
        Item.PrintTime = message.Value.PrintStatus.PrintTime;
        Item.PrintTimeTotal = message.Value.PrintStatus.PrintTimeTotal;
        Item.State = message.Value.State;

        Item.SetConditions(message.Value.Conditions);
    }

    /// <summary>
    /// Ends the "just started" window once the printer confirms the job, or drops the optimistic file name
    /// when the printer still reports an idle state after <see cref="StartGrace" /> (the job was rejected).
    /// </summary>
    private void ResolveStartedFileName(UniversalPrinterState state)
    {
        if (_startedFileName is null)
        {
            return;
        }

        if (state is UniversalPrinterState.Preparing or UniversalPrinterState.Printing or UniversalPrinterState.Paused)
        {
            _startedFileName = null;
        }
        else if (_timeProvider.GetUtcNow() - _startedAt > StartGrace)
        {
            Item.PrintFileName = _previousFileName;
            _startedFileName = null;
        }
    }

    private async Task LoadData(CancellationToken cancellationToken)
    {
        if (!Item.IsConnectorAvailable)
        {
            // No connector is loaded for this printer, so there is nothing to ask for its state.
            return;
        }

        try
        {
            await _agentCommands.TriggerInitialStateAsync(Item.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to trigger initial state for printer {PrinterId}", Item.Id);
        }
    }

    [RelayCommand]
    private void OpenPage() => WeakReferenceMessenger.Default.Send(new OpenPrinterPageRequested(this));

    /// <summary>The connector's display name, or its id while no connector with that id is loaded.</summary>
    internal string ConnectorName =>
        _agentCommands.Connectors.FirstOrDefault(c => string.Equals(c.Id, Item.ConnectorId, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? Item.ConnectorId;

    /// <summary>True when the printer's connector can report a console. Printers without one show no Console tab.</summary>
    internal bool SupportsConsole => Supports(ConnectorCapabilities.Console);

    /// <summary>True when the printer's connector can take commands typed in its console (the console must report as well).</summary>
    internal bool SupportsConsoleCommands => Supports(ConnectorCapabilities.Console | ConnectorCapabilities.ConsoleCommands);

    /// <summary>The console of this printer, for the printer page. Loads nothing until asked.</summary>
    internal PrinterConsoleViewModel CreateConsoleViewModel() =>
        new(Item.Id, _agentCommands, _loggerFactory.CreateLogger<PrinterConsoleViewModel>(), SupportsConsoleCommands);

    private bool Supports(ConnectorCapabilities capabilities) =>
        _agentCommands.Connectors.FirstOrDefault(c => string.Equals(c.Id, Item.ConnectorId, StringComparison.OrdinalIgnoreCase))
            ?.Capabilities.HasFlag(capabilities) == true;

    [RelayCommand]
    private async Task ShowEvents()
    {
        var events = CreateEventsViewModel();
        await this.ShowOverlayDialogAsync($"{Item.Name} events", events);
    }

    [RelayCommand]
    private async Task ShowPrinterFiles(CancellationToken cancellationToken)
    {
        var dashboardViewModel = CreateFilesViewModel();
        _ = dashboardViewModel.LoadDataCommand.ExecuteAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        var result = await this.ShowOverlayDialogAsync(null, dashboardViewModel);
        if (result is null)
        {
            return;
        }
    }

    [RelayCommand]
    private async Task StartPauseButtonClicked(CancellationToken cancellationToken)
    {
        try
        {
            if (Item.State == UniversalPrinterState.Printing)
            {
                _logger.LogInformation("User requested pause on printer {PrinterId} ('{PrinterName}')", Item.Id, Item.Name);
                await _agentCommands.PausePrintJobAsync(Item.Id, cancellationToken);
            }
            else if (Item.State == UniversalPrinterState.Paused)
            {
                _logger.LogInformation("User requested resume on printer {PrinterId} ('{PrinterName}')", Item.Id, Item.Name);
                await _agentCommands.ResumePrintJobAsync(Item.Id, cancellationToken);
            }
            else if (Item.State is UniversalPrinterState.Idle or UniversalPrinterState.Completed
                     or UniversalPrinterState.Cancelled or UniversalPrinterState.Failed)
            {
                var dashboardViewModel = CreateFilesViewModel();
                _ = dashboardViewModel.LoadDataCommand.ExecuteAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                var result = await this.ShowOverlayDialogAsync(null, dashboardViewModel);
                if (result is null)
                {
                    return;
                }

                await StartFileCoreAsync(result.FileName, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to execute Start/Pause command on printer {PrinterId} ('{PrinterName}')", Item.Id, Item.Name);
            throw;
        }
    }

    /// <summary>Starts printing a file that is already on the printer. Used by the printer page's file list.</summary>
    [RelayCommand]
    private async Task StartFile(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            await StartFileCoreAsync(fileName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to start file '{FileName}' on printer {PrinterId} ('{PrinterName}')", fileName, Item.Id, Item.Name);
            throw;
        }
    }

    private async Task StartFileCoreAsync(string fileName, CancellationToken cancellationToken)
    {
        _logger.LogInformation("User starting print job '{FileName}' on printer {PrinterId} ('{PrinterName}')",
            fileName, Item.Id, Item.Name);
        await _agentCommands.StartPrintJobAsync(Item.Id, fileName, cancellationToken);

        // Printers report the file name late (often only once printing), so show the chosen one while preparing.
        _previousFileName = Item.PrintFileName;
        _startedFileName = fileName;
        _startedAt = _timeProvider.GetUtcNow();
        Item.PrintFileName = fileName;
        _ = TryGetThumbnail();
    }

    /// <summary>The history of this printer, for the dialog and for the printer page. Loads nothing until asked.</summary>
    internal PrinterEventsViewModel CreateEventsViewModel() =>
        new(Item.Id, Item.Name, _agentCommands, _loggerFactory.CreateLogger<PrinterEventsViewModel>());

    /// <summary>The files on this printer, for the file dialog and for the printer page. Loads nothing until asked.</summary>
    internal FilePickerViewModel CreateFilesViewModel() =>
        new(Item.Id, _agentCommands, _imageCache, _loggerFactory.CreateLogger<FilePickerViewModel>());

    [RelayCommand]
    private async Task StopButtonClicked(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("User requested stop print job on printer {PrinterId} ('{PrinterName}')", Item.Id, Item.Name);
            await _agentCommands.StopPrintJobAsync(Item.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to stop print job on printer {PrinterId} ('{PrinterName}')", Item.Id, Item.Name);
            throw;
        }
    }

    private async Task TryGetThumbnail()
    {
        if (string.IsNullOrWhiteSpace(Item.PrintFileName))
        {
            return;
        }

        try
        {
            if (_imageCache.TryGetBytes(Item.PrintFileName, out var bytes) && bytes is not null)
            {
                Item.ThumbnailData = bytes;
            }
            else
            {
                var response = await _agentCommands.GetThumbnailAsync(Item.Id, Item.PrintFileName, CancellationToken.None);
                if (response is not null)
                {
                    _imageCache.Add(Item.PrintFileName, response.Data);
                    Item.ThumbnailData = response.Data;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load thumbnail for file '{FileName}' on printer {PrinterId}", Item.PrintFileName, Item.Id);
        }
    }
}
