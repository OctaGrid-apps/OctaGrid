using System.Collections.ObjectModel;
using System.ComponentModel;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.ViewModels;

public class PrinterStatusWidget : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public double BedTargetTemperature
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(BedTargetTemperature));
            }
        }
    }

    public double BedTemperature
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(BedTemperature));
            }
        }
    }

    // Completed/Cancelled/Failed are last-job results: the printer is ready, so a new print can start.
    public bool CanStartPause => IsConnectorAvailable && State is UniversalPrinterState.Idle or UniversalPrinterState.Completed
                                   or UniversalPrinterState.Cancelled or UniversalPrinterState.Failed
                                   or UniversalPrinterState.Printing or UniversalPrinterState.Paused;

    /// <summary>True when a new print can be started: the printer is ready, not busy with a job.</summary>
    public bool CanStartNewPrint => IsConnectorAvailable && State is UniversalPrinterState.Idle or UniversalPrinterState.Completed
                                        or UniversalPrinterState.Cancelled or UniversalPrinterState.Failed;

    public bool CanStop => IsConnectorAvailable && State is UniversalPrinterState.Preparing or UniversalPrinterState.Printing
                                  or UniversalPrinterState.Paused;

    public int? CurrentLayer
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(CurrentLayer));
            }
        }
    }

    public Guid Id { get; set; }

    /// <summary>Id of the connector that talks to the printer; used to filter the dashboard.</summary>
    public string ConnectorId { get; set; } = string.Empty;

    /// <summary>False when the connector is switched off or removed, so the printer cannot connect.</summary>
    public bool IsConnectorAvailable
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IsConnectorAvailable));
                OnPropertyChanged(nameof(CanStartNewPrint));
                OnPropertyChanged(nameof(CanStartPause));
                OnPropertyChanged(nameof(CanStop));
            }
        }
    } = true;

    public bool IsPaused => State == UniversalPrinterState.Paused;

    public bool IsPrinting => State == UniversalPrinterState.Printing;

    public string Name
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    } = string.Empty;

    public double NozzleTargetTemperature
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(NozzleTargetTemperature));
            }
        }
    }

    public double NozzleTemperature
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(NozzleTemperature));
            }
        }
    }

    public string? PrintFileName
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(PrintFileName));
            }
        }
    }

    public TimeOnly PrintTime
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(PrintTime));
                OnPropertyChanged(nameof(PrintTimeLeft));
            }
        }
    }

    public TimeOnly PrintTimeLeft => TimeOnly.FromTimeSpan(PrintTimeTotal - PrintTime);

    public TimeOnly PrintTimeTotal
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(PrintTimeTotal));
            }
        }
    }

    public double Progress
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Progress));
            }
        }
    }

    public string StartPauseLabel => State switch
    {
        UniversalPrinterState.Printing => "Pause",
        UniversalPrinterState.Paused => "Resume",
        _ => "Start"
    };

    /// <summary>False until the printer has reported a state, so the default <see cref="State" /> is not mistaken for a real Offline.</summary>
    public bool IsStateKnown { get; private set; }

    public UniversalPrinterState State
    {
        get;
        set
        {
            var firstReport = !IsStateKnown;
            IsStateKnown = true;
            if (field != value || firstReport)
            {
                field = value;
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(IsPrinting));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(CanStartNewPrint));
                OnPropertyChanged(nameof(CanStartPause));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(StartPauseLabel));
            }
        }
    }

    /// <summary>What is wrong with the printer right now, most serious first.</summary>
    public ObservableCollection<string> ConditionMessages
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(ConditionMessages));
                OnPropertyChanged(nameof(ConditionMessage));
            }
        }
    } = [];

    /// <summary>
    /// Shows the given conditions, most serious first. Snapshots arrive about once a second and almost always repeat
    /// the same conditions, so the list is replaced (and bindings notified) only when the messages differ.
    /// </summary>
    public void SetConditions(IReadOnlyList<PrinterCondition> conditions)
    {
        if (conditions.Count == 0 && ConditionMessages.Count == 0)
        {
            return;
        }

        var messages = conditions.OrderByDescending(c => c.Severity).Select(c => c.Message).ToList();
        if (!messages.SequenceEqual(ConditionMessages))
        {
            ConditionMessages = new ObservableCollection<string>(messages);
        }
    }

    /// <summary>The most serious condition, or null when nothing is wrong.</summary>
    public string? ConditionMessage => ConditionMessages.FirstOrDefault();

    public byte[] ThumbnailData
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(ThumbnailData));
        }
    } = [];

    public int? TotalLayerCount
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(TotalLayerCount));
            }
        }
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
