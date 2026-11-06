using FluentValidation;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Validation;

public class CreatePrinterRequestValidator : AbstractValidator<CreatePrinterRequest>
{
    public CreatePrinterRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        // One message per property: the service reports validation failures keyed by property name.
        RuleFor(x => x.ConnectorId).Cascade(CascadeMode.Stop).NotEmpty().Must(ExtensionId.IsValid).WithMessage("The connector id must look like 'publisher.name'.");

        // Which settings a connector needs is declared by the connector; the connector checks
        // them when it connects, so only the shape is validated here.
        RuleFor(x => x.Settings).NotNull();
        RuleFor(x => x.Secrets).NotNull();
    }
}
