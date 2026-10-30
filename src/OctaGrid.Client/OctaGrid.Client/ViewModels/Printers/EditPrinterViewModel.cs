using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class EditPrinterViewModel : ViewModelBase, IDialogParticipant<Printer>
{
    private readonly IAgentCommandsService _agentCommands;
    private readonly ILogger<EditPrinterViewModel> _logger;

    // Design-time only — see MainViewModel's parameterless constructor for why this exists.
    public EditPrinterViewModel() : this(
        new PrinterViewModel(new Printer()),
        App.Services.GetRequiredService<IAgentCommandsService>(),
        App.Services.GetService<ILogger<EditPrinterViewModel>>() ?? NullLogger<EditPrinterViewModel>.Instance)
    {
    }

    public EditPrinterViewModel(
        PrinterViewModel printer,
        IAgentCommandsService agentCommands,
        ILogger<EditPrinterViewModel>? logger = null)
    {
        Item = printer;
        _agentCommands = agentCommands;
        _logger = logger ?? NullLogger<EditPrinterViewModel>.Instance;
        PrinterConnectionProtocols = [
            "Moonraker",
            "BambuMqtt",
            "PrusaLink"
            ];

        if (string.IsNullOrWhiteSpace(Item.ConnectionProtocol))
        {
            Item.ConnectionProtocol = PrinterConnectionProtocols.First();
        }
        Item.PropertyChanged += Item_PropertyChanged;
    }

    public bool CanTestPritnerConnection => Item.CanTestConnection();

    public bool HasTestResult => !string.IsNullOrWhiteSpace(TestResultMessage);

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    public partial PrinterViewModel Item { get; private set; }

    [ObservableProperty]
    public partial List<string> PrinterConnectionProtocols { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTestResult))]
    public partial string? TestResultMessage { get; set; }

    // Reuses the printer-state brushes (green/red already mean "good"/"bad" there) instead of
    // introducing a second success/error color pair just for this.
    public string TestResultStateKey => TestSucceeded ? "Printing" : "Error";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestResultStateKey))]
    public partial bool TestSucceeded { get; set; }

    [RelayCommand]
    private void Cancel()
    {
        _logger.LogDebug("EditPrinter dialog cancelled by user");
        this.ReturnResultFromOverlayDialog(null);
    }

    [RelayCommand]
    private void Confirm()
    {
        Item.Validate();
        if (!Item.HasErrors)
        {
            _logger.LogDebug("EditPrinter dialog confirmed for printer '{PrinterName}' ({Protocol} @ {Endpoint})",
                Item.Name, Item.ConnectionProtocol, Item.Address);
            this.ReturnResultFromOverlayDialog(Item.ToPrinter());
        }
    }

    private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Item.Address) or nameof(Item.ConnectionProtocol) or nameof(Item.ApiKey))
        {
            OnPropertyChanged(nameof(CanTestPritnerConnection));
        }
    }

    [RelayCommand]
    private async Task TestConnection(CancellationToken cancellationToken)
    {
        IsTesting = true;
        TestResultMessage = null;
        try
        {
            var result = await _agentCommands.TestConnectionAsync(
                Item.ConnectionProtocol, Item.Address, Item.ApiKey, cancellationToken);
            TestSucceeded = result.Success;
            TestResultMessage = result.Success ? "Connected successfully." : result.Error ?? "Connection failed.";
        }
        catch (OperationCanceledException)
        {
            // Dialog closed mid-test — nothing to show.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test threw an exception for '{Endpoint}' ({Protocol})", Item.Address, Item.ConnectionProtocol);
            TestSucceeded = false;
            TestResultMessage = ex.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }
}
