using FluentValidation;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Application.Validation;

public class UpdateAgentRequestValidator : AbstractValidator<UpdateAgentRequest>
{
    public UpdateAgentRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);
    }
}
