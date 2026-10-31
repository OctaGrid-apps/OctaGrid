using FluentValidation;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Application.Validation;

public class UpdateDashboardRequestValidator : AbstractValidator<UpdateDashboardRequest>
{
    public UpdateDashboardRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
