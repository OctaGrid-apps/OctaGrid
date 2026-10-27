using FluentValidation;
using OctaGrid.Data.Abstractions.Models;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Validation;

public class UpdatePrinterRequestValidator : AbstractValidator<UpdatePrinterRequest>
{
    public UpdatePrinterRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Url).NotEmpty();
        RuleFor(x => x.ConnectionProtocol).NotEmpty().IsEnumName(typeof(PrinterProtocol));
        RuleFor(x => x.ApiKey).NotEmpty().When(x => !string.IsNullOrEmpty(x.ApiKey));
    }
}
