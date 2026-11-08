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
    public bool IsConnectorAvailable { get; set; } = true;

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
                OnPropertyChanged(nameof(CanStartPause));
                OnPropertyChanged(nameof(CanStop));
            }
        }
    }

    public string? StateMessage => StateMessages.FirstOrDefault();

    public ObservableCollection<string> StateMessages
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(StateMessages));
                OnPropertyChanged(nameof(StateMessage));
            }
        }
    } = [];

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
