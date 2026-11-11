using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.ViewModels;

public partial class EditPrinterViewModel : ViewModelBase, IDialogParticipant<Printer>
{
    private readonly IAgentCommandsService _agentCommands;
    private readonly ILogger<EditPrinterViewModel> _logger;

    public EditPrinterViewModel(
        PrinterViewModel printer,
        IAgentCommandsService agentCommands,
        ILogger<EditPrinterViewModel>? logger = null)
    {
        Item = printer;
        _agentCommands = agentCommands;
        _logger = logger ?? NullLogger<EditPrinterViewModel>.Instance;
        Connectors = agentCommands.Connectors;
        Item.SetConnectors(Connectors);

        if (string.IsNullOrWhiteSpace(Item.ConnectorId) && Connectors.Count > 0)
        {
            Item.ConnectorId = Connectors[0].Id;
        }
        Item.PropertyChanged += OnItemPropertyChanged;
        Item.ErrorsChanged += OnItemErrorsChanged;
        Item.FieldsChanged += OnItemFieldsChanged;
    }

    private void OnItemFieldsChanged(object? sender, EventArgs e) => TestConnectionCommand.NotifyCanExecuteChanged();

    private void OnItemErrorsChanged(object? sender, System.ComponentModel.DataErrorsChangedEventArgs e)
    {
        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    public bool HasTestResult => !string.IsNullOrWhiteSpace(TestResultMessage);

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    public partial PrinterViewModel Item { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ConnectorDescriptor> Connectors { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTestResult))]
    public partial string? TestResultMessage { get; set; }

    // Reuses the printer-state brushes (green/red already mean "good"/"bad" there) instead of
    // introducing a second success/error color pair just for this.
    public string TestResultStateKey => TestSucceeded ? "Printing" : "Error";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestResultStateKey))]
    public partial bool TestSucceeded { get; set; }

    public bool CanTestPritnerConnection() => Item.CanTestConnection();

    [RelayCommand]
    private void Cancel()
    {
        _logger.LogDebug("EditPrinter dialog cancelled by user");
        this.ReturnResultFromOverlayDialog(null);
        Cleanup();
    }

    [RelayCommand]
    private void Confirm()
    {
        var valid = Item.ValidateAll();
        if (valid)
        {
            _logger.LogDebug("EditPrinter dialog confirmed for printer '{PrinterName}' ({ConnectorId})",
                Item.Name, Item.ConnectorId);
            this.ReturnResultFromOverlayDialog(Item.ToPrinter());
            Cleanup();
        }
    }

    private void Cleanup()
    {
        if (Item is not null)
        {
            Item.ErrorsChanged -= OnItemErrorsChanged;
            Item.FieldsChanged -= OnItemFieldsChanged;
            Item.PropertyChanged -= OnItemPropertyChanged;
        }
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Item.ConnectorId))
        {
            OnPropertyChanged(nameof(CanTestPritnerConnection));
        }
    }

    [RelayCommand(CanExecute = nameof(CanTestPritnerConnection))]
    private async Task TestConnection(CancellationToken cancellationToken)
    {
        IsTesting = true;
        TestResultMessage = null;
        try
        {
            var result = await _agentCommands.TestConnectionAsync(
                Item.ConnectorId, Item.CollectSettings(), Item.SavedPrinterId, cancellationToken);
            TestSucceeded = result.Success;
            TestResultMessage = result.Success ? "Connected successfully." : result.Error ?? "Connection failed.";
        }
        catch (OperationCanceledException)
        {
            // Dialog closed mid-test — nothing to show.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test threw an exception for connector {ConnectorId}", Item.ConnectorId);
            TestSucceeded = false;
            TestResultMessage = ex.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }
}
