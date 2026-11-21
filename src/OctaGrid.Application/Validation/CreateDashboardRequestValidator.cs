using FluentValidation;
using OctaGrid.Protocol.Dashboards;

namespace OctaGrid.Application.Validation;

public class CreateDashboardRequestValidator : AbstractValidator<CreateDashboardRequest>
{
    public CreateDashboardRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
