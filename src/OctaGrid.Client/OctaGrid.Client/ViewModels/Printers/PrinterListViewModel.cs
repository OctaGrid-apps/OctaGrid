using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Controls.Dialogs;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.ViewModels;

public partial class PrinterListViewModel : ViewModelBase, IDialogParticipant
{
    private readonly ApplicationState _appState;
    private readonly ILogger<PrinterListViewModel> _logger;
    private readonly INotificationService _notification;
    private readonly IPrintersClient _printersClient;

    public PrinterListViewModel(
        IPrintersClient printersClient,
        INotificationService notification,
        ApplicationState appState,
        ILogger<PrinterListViewModel>? logger = null)
    {
        _printersClient = printersClient;
        _notification = notification;
        _appState = appState;
        _logger = logger ?? NullLogger<PrinterListViewModel>.Instance;
        WeakReferenceMessenger.Default.Register<PrinterEntityUpsert>(this, HandlePrinterEntityUpsert);
        WeakReferenceMessenger.Default.Register<PrinterEntityDelete>(this, HandlePrinterEntityDelete);
    }

    [ObservableProperty]
    public partial ObservableCollection<IPrinterListItem> Printers { get; set; } = [];

    [RelayCommand]
    private async Task ChangePrinterEnabled(PrinterListItem printer, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Changing printer {PrinterId} ('{PrinterName}') enabled state to {Enabled}",
                printer.Id, printer.Name, printer.Enabled);
            await _printersClient.SetPrinterEnabled(printer.Id, printer.Enabled, cancellationToken);
            _notification.ShowSuccess(printer.Name, printer.Enabled ? "Printer enabled." : "Printer disabled.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to change enabled state for printer {PrinterId}", printer.Id);
            throw;
        }
    }

    [RelayCommand]
    private async Task CreatePrinter(CancellationToken cancellationToken)
    {
        var printerViewModel = new EditPrinterViewModel();

        var result = await this.ShowOverlayDialogAsync("Create new printer", printerViewModel);
        if (result is null)
        {
            return;
        }

        var workspaceId = _appState.GetWorkspaceId();

        try
        {
            _logger.LogInformation("Creating printer '{PrinterName}' ({Protocol} @ {Endpoint}) in workspace {WorkspaceId}",
                result.Name, result.ConnectorId, result.Url, workspaceId);
            var request = new CreatePrinterRequest(result.Name, result.Url, result.ConnectorId, result.ApiKey, result.Enabled);
            _ = await _printersClient.CreatePrinter(workspaceId, request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to create printer '{PrinterName}' in workspace {WorkspaceId}", result.Name, workspaceId);
            throw;
        }
    }

    [RelayCommand]
    private async Task DeletePrinter(PrinterListItem printer, CancellationToken cancellationToken)
    {
        var result = await this.ShowOverlayDialogAsync<DialogResult>("Delete printer",
            $"Are you sure you want to delete '{printer.Name}' printer?",
            DialogCommands.YesNo);
        if (result == DialogResult.Yes)
        {
            try
            {
                _logger.LogInformation("Deleting printer {PrinterId} ('{PrinterName}')", printer.Id, printer.Name);
                await _printersClient.DeletePrinter(printer.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to delete printer {PrinterId} ('{PrinterName}')", printer.Id, printer.Name);
                throw;
            }
        }
    }

    [RelayCommand]
    private async Task EditPrinter(PrinterListItem printer, CancellationToken cancellationToken)
    {
        var printerViewModel = new EditPrinterViewModel(new PrinterViewModel(new Printer()
        {
            Id = printer.Id,
            Name = printer.Name,
            Url = printer.Url,
            ConnectorId = printer.ConnectorId,
            Enabled = printer.Enabled,
            ApiKey = printer.EncryptedApiKey,
        }), App.Services.GetRequiredService<IAgentCommandsService>());

        var result = await this.ShowOverlayDialogAsync("Edit printer", printerViewModel);
        if (result is null)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Updating printer {PrinterId} ('{PrinterName}', {Protocol} @ {Endpoint})",
                printer.Id, result.Name, result.ConnectorId, result.Url);
            _ = await _printersClient.UpdatePrinter(printer.Id, new UpdatePrinterRequest(result.Name, result.Url, result.ConnectorId, result.ApiKey, result.Enabled), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to update printer {PrinterId}", printer.Id);
            throw;
        }
    }

    private void HandlePrinterEntityDelete(object recipient, PrinterEntityDelete message)
    {
        if (_appState.SelectedWorkspace is null)
        {
            return;
        }

        var printer = Printers.OfType<PrinterListItem>().FirstOrDefault(p => p.Id == message.Value.EntityId);
        if (printer is not null && printer.Version < message.Value.Version)
        {
            return;
        }

        if (printer is not null)
        {
            Printers.Remove(printer);
        }
    }

    private async void HandlePrinterEntityUpsert(object recipient, PrinterEntityUpsert message)
    {
        if (_appState.SelectedWorkspace is null)
        {
            return;
        }

        var printer = Printers.OfType<PrinterListItem>().FirstOrDefault(p => p.Id == message.Value.EntityId);
        if (printer is not null && printer.Version >= message.Value.Version)
        {
            return;
        }

        try
        {
            var workspaceId = _appState.GetWorkspaceId();
            var response = await _printersClient.GetPrinter(workspaceId, message.Value.EntityId, CancellationToken.None);

            if (printer is null)
            {
                var insertIndex = Printers.IndexOf(AddPrinterListItem.Instance);
                Printers.Insert(insertIndex, response.ToPrinterListItem());
            }
            else
            {
                Printers[Printers.IndexOf(printer)] = response.ToPrinterListItem();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle printer upsert notification for printer {PrinterId}", message.Value.EntityId);
        }
    }

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        if (_appState.SelectedWorkspace is null)
        {
            _logger.LogWarning("PrinterListViewModel LoadData skipped: no workspace selected");
            return;
        }

        var workspaceId = _appState.GetWorkspaceId();
        try
        {
            var response = await _printersClient.GetPrintersList(workspaceId, new GetPrintersQuery(Enabled: null), cancellationToken);
            IEnumerable<IPrinterListItem> items = response.Select(p => p.ToPrinterListItem());
            Printers = new ObservableCollection<IPrinterListItem>(items.Append(AddPrinterListItem.Instance));
            _logger.LogDebug("Loaded {PrinterCount} printer(s) for workspace {WorkspaceId}", response.Count, workspaceId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to load printers for workspace {WorkspaceId}", workspaceId);
            throw;
        }
    }
}
