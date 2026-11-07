using FluentValidation;
using OctaGrid.Protocol.Dashboards;

namespace OctaGrid.Application.Validation;

public class UpdateDashboardRequestValidator : AbstractValidator<UpdateDashboardRequest>
{
    public UpdateDashboardRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
