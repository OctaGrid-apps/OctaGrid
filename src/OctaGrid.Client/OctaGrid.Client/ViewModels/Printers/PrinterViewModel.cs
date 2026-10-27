using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using OctaGrid.Client.Models;

namespace OctaGrid.Client.ViewModels;

public partial class PrinterViewModel : ViewModelValidatorBase
{
    public PrinterViewModel(Printer printer)
    {
        Id = printer.Id;
        Name = printer.Name;
        Address = printer.Url;
        ApiKey = printer.ApiKey;
        IsEnabled = printer.Enabled;
        ConnectionProtocol = printer.ConnectionProtocol;
    }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required, Length(2, 500)]
    public partial string Address { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [RegularExpression(@"^$|^.{2,1000}$")]
    public partial string? ApiKey { get; set; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required, Length(2, 50)]
    public partial string ConnectionProtocol { get; set; }

    [ObservableProperty]
    public partial Guid? Id { get; private set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required, Length(2, 100)]
    public partial string Name { get; set; }

    public bool CanTestConnection()
    {
        if (HasErrors)
        {
            var errors = GetErrors();
            return !errors.Any(x => x.MemberNames.Any(m => m == nameof(Address) || m == nameof(ConnectionProtocol) || m == nameof(ApiKey)));
        }
        return true;
    }

    public Printer ToPrinter() => new()
    {
        Id = Id ?? Guid.Empty,
        Name = Name ?? "",
        Url = Address ?? "",
        ConnectionProtocol = ConnectionProtocol ?? "",
        Enabled = IsEnabled
    };
}
