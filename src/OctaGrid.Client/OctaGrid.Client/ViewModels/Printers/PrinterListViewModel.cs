using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Controls.Dialogs;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Client.ViewModels;

public partial class PrinterListViewModel : ViewModelBase, IDialogParticipant
{
    private readonly ApplicationState _appState;
    private readonly INotificationService _notification;
    private readonly IPrintersClient _printersClient;

    public PrinterListViewModel(
        IPrintersClient printersClient,
        INotificationService notification,
        ApplicationState appState)
    {
        _printersClient = printersClient;
        _notification = notification;
        _appState = appState;
        WeakReferenceMessenger.Default.Register<PrinterEntityUpsert>(this, HandlePrinterEntityUpsert);
        WeakReferenceMessenger.Default.Register<PrinterEntityDelete>(this, HandlePrinterEntityDelete);
    }

    [ObservableProperty]
    public partial ObservableCollection<IPrinterListItem> Printers { get; set; } = [];

    [RelayCommand]
    private async Task ChangePrinterEnabled(PrinterListItem printer, CancellationToken cancellationToken)
    {
        await _printersClient.SetPrinterEnabled(printer.Id, printer.Enabled, cancellationToken);
        _notification.ShowSuccess(printer.Name, printer.Enabled ? "Printer enabled." : "Printer disabled.");
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

        var request = new CreatePrinterRequest(result.Name, result.Url, result.ConnectionProtocol, result.ApiKey, result.Enabled);
        _ = await _printersClient.CreatePrinter(workspaceId, request, cancellationToken);
    }

    [RelayCommand]
    private async Task DeletePrinter(PrinterListItem printer, CancellationToken cancellationToken)
    {
        var result = await this.ShowOverlayDialogAsync<DialogResult>("Delete printer",
         $"Are you sure you want to delete '{printer.Name}' printer?",
         DialogCommands.YesNo);
        if (result == DialogResult.Yes)
        {
            await _printersClient.DeletePrinter(printer.Id, cancellationToken);
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
            ConnectionProtocol = printer.ConnectionProtocol,
            Enabled = printer.Enabled,
            ApiKey = printer.EncryptedApiKey,
        }), App.Services.GetRequiredService<IAgentCommandsService>());

        var result = await this.ShowOverlayDialogAsync("Edit printer", printerViewModel);
        if (result is null)
        {
            return;
        }

        _ = await _printersClient.UpdatePrinter(printer.Id, new UpdatePrinterRequest(result.Name, result.Url, result.ConnectionProtocol, result.ApiKey, result.Enabled), cancellationToken);
    }

    private async void HandlePrinterEntityDelete(object recipient, PrinterEntityDelete message)
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

    [RelayCommand]
    private async Task LoadData(CancellationToken cancellationToken)
    {
        if (_appState.SelectedWorkspace is null)
        {
            return;
        }

        var workspaceId = _appState.GetWorkspaceId();
        var response = await _printersClient.GetPrintersList(workspaceId, new GetPrintersQuery(Enabled: null), cancellationToken);
        IEnumerable<IPrinterListItem> items = response.Select(p => p.ToPrinterListItem());
        Printers = new ObservableCollection<IPrinterListItem>(items.Append(AddPrinterListItem.Instance));
    }
}
