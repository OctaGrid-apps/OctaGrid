using FluentValidation;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Validation;

public class UpdatePrinterIdentityRequestValidator : AbstractValidator<UpdatePrinterIdentityRequest>
{
    public UpdatePrinterIdentityRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}
