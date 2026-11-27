using FluentValidation;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Application.Validation;

/// <summary>
/// What the user chose when starting a print. Only the shape is checked here; the connector checks the values against
/// the file and the printer, because only it knows them.
/// </summary>
public class PrintStartChoicesValidator : AbstractValidator<PrintStartChoices>
{
    /// <summary>The most start options one start may carry.</summary>
    public const int MaxOptions = 32;

    public PrintStartChoicesValidator()
    {
        RuleFor(x => x.Plate)
            .InclusiveBetween(1, 1000)
            .When(x => x.Plate is not null)
            .WithMessage("Choose one of the file's plates.");
        RuleFor(x => x.Options)
            .Must(o => o is null || o.Count <= MaxOptions)
            .WithMessage($"Send at most {MaxOptions} start options.");
        RuleForEach(x => x.Options)
            .Must(o => o.Key.Length is > 0 and <= 64 && o.Value.Length <= 256)
            .WithMessage("A start option is too long.")
            .OverridePropertyName("options");
    }
}
