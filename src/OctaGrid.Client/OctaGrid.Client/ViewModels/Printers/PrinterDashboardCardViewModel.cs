using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.ViewModels;

public partial class PrinterDashboardCardViewModel : ViewModelBase, IDialogParticipant
{
    private readonly IAgentCommandsService _agentCommands;
    private readonly IByteArrayImageCache _imageCache;

    public PrinterDashboardCardViewModel(PrinterStatusWidget item)
    {
        Item = item;
        _agentCommands = App.Services.GetRequiredService<IAgentCommandsService>();
        _imageCache = App.Services.GetRequiredService<IByteArrayImageCache>();

        WeakReferenceMessenger.Default.Register<PrinterTelemetryData, Guid>(this, item.Id, HandlePrinterTelemetry);
        _ = LoadData(CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    [ObservableProperty]
    public partial PrinterStatusWidget Item { get; private set; }

    private async void HandlePrinterTelemetry(object recipient, PrinterTelemetryData message)
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

        if (message.Value.PrintStatus.FileName is not null)
        {
            Item.PrintFileName = message.Value.PrintStatus.FileName;
            _ = TryGetThumbnail().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
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

        Item.StateMessages = new ObservableCollection<string>(message.Value.StateMessages.Select(sm => sm.Message));
    }

    private Task LoadData(CancellationToken cancellationToken)
    {
        return _agentCommands.TriggerInitialStateAsync(Item.Id, cancellationToken);
    }

    [RelayCommand]
    private async Task ShowPrinterFiles(CancellationToken cancellationToken)
    {
        var dashboardViewModel = new FilePickerViewModel(Item.Id);
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
        if (Item.State == UniversalPrinterState.Printing)
        {
            await _agentCommands.PausePrintJobAsync(Item.Id, cancellationToken);
        }
        else if (Item.State == UniversalPrinterState.Paused)
        {
            await _agentCommands.ResumePrintJobAsync(Item.Id, cancellationToken);
        }
        else if (Item.State is UniversalPrinterState.Idle or UniversalPrinterState.Completed
                 or UniversalPrinterState.Cancelled or UniversalPrinterState.Failed)
        {
            var dashboardViewModel = new FilePickerViewModel(Item.Id);
            _ = dashboardViewModel.LoadDataCommand.ExecuteAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            var result = await this.ShowOverlayDialogAsync(null, dashboardViewModel);
            if (result is null)
            {
                return;
            }

            await _agentCommands.StartPrintJobAsync(Item.Id, result.FileName, cancellationToken);
        }
    }

    [RelayCommand]
    private async Task StopButtonClicked(CancellationToken cancellationToken)
    {
        await _agentCommands.StopPrintJobAsync(Item.Id, cancellationToken);
    }

    private async Task TryGetThumbnail()
    {
        if (string.IsNullOrWhiteSpace(Item.PrintFileName))
        {
            return;
        }

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
}
