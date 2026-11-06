using FluentValidation;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Validation;

public class CreatePrinterRequestValidator : AbstractValidator<CreatePrinterRequest>
{
    public CreatePrinterRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Url).NotEmpty();
        RuleFor(x => x.ConnectorId).NotEmpty().Must(ExtensionId.IsValid).WithMessage("The connector id must look like 'publisher.name'.");
        RuleFor(x => x.ApiKey).NotEmpty().When(x => !string.IsNullOrEmpty(x.ApiKey));
    }
}
