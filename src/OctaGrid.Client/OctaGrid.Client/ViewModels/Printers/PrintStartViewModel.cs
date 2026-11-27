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

/// <summary>
/// Asked before a print starts: which plate, with which options, and that the build plate is clear. Starting a print
/// moves the printer, so nothing starts until the user confirms here. Printers whose connector offers no choices get
/// only the confirmation.
/// </summary>
public partial class PrintStartViewModel : ViewModelBase, IDialogParticipant<PrintStartChoices>
{
    private readonly IAgentCommandsService _commands;
    private readonly ILogger<PrintStartViewModel> _logger;
    private readonly Guid _printerId;
    private PrintStartPlan? _plan;

    public PrintStartViewModel(
        Guid printerId,
        string printerName,
        string fileName,
        IAgentCommandsService commands,
        ILogger<PrintStartViewModel>? logger = null)
    {
        _printerId = printerId;
        PrinterName = printerName;
        FileName = fileName;
        _commands = commands;
        _logger = logger ?? NullLogger<PrintStartViewModel>.Instance;
    }

    public string PrinterName { get; }

    public string FileName { get; }

    public ObservableCollection<PrintPlateViewModel> Plates { get; } = [];

    public ObservableCollection<SettingFieldViewModel> Options { get; } = [];

    /// <summary>Several plates to choose from; a file with one plate shows no choice.</summary>
    public bool HasPlateChoice => Plates.Count > 1;

    public bool HasOptions => Options.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>The plan is loaded (or the connector offers none), so the choices can be shown.</summary>
    public bool IsReady => !IsLoading && !HasError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial PrintPlateViewModel? SelectedPlate { get; set; }

    /// <summary>The user confirmed that nothing is left on the build plate.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsPlateClear { get; set; }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            _plan = await _commands.GetPrintStartPlanAsync(_printerId, FileName, cancellationToken);
            Plates.Clear();
            Options.Clear();
            if (_plan is not null)
            {
                foreach (var plate in _plan.Plates)
                {
                    Plates.Add(new PrintPlateViewModel(plate));
                }

                foreach (var option in _plan.Options)
                {
                    Options.Add(new SettingFieldViewModel(option, option.DefaultValue ?? string.Empty));
                }

                // The first plate that can start, so a click on Start does what the user most likely wants.
                SelectedPlate = Plates.FirstOrDefault(p => p.CanStart) ?? Plates.FirstOrDefault();
            }

            OnPropertyChanged(nameof(HasPlateChoice));
            OnPropertyChanged(nameof(HasOptions));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AppException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read how to start '{FileName}' on printer {PrinterId}", FileName, _printerId);
            ErrorMessage = "OctaGrid could not read the file from the printer. Check the printer and try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanStart() =>
        IsReady && IsPlateClear && (_plan is null || SelectedPlate is { CanStart: true });

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (!CanStart())
        {
            return;
        }

        this.ReturnResultFromOverlayDialog(Choices());
    }

    /// <summary>What the user chose; for a connector without choices, an empty set.</summary>
    internal PrintStartChoices Choices() =>
        _plan is null
            ? new PrintStartChoices()
            : new PrintStartChoices(
                SelectedPlate?.Number,
                Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal));

    [RelayCommand]
    private void Cancel() => this.ReturnResultFromOverlayDialog(null);
}
