using FluentValidation;

namespace OctaGrid.Application.Validation;

/// <summary>
/// The files whose details are asked for at once. Bounded, because the agent reads each of them from the printer.
/// </summary>
public class FileDetailsRequestValidator : AbstractValidator<IReadOnlyList<string>>
{
    /// <summary>The most files one request may name; the client asks for the rows it shows, a few at a time.</summary>
    public const int MaxFiles = 50;

    public FileDetailsRequestValidator()
    {
        RuleFor(x => x)
            .NotEmpty()
            .Must(x => x.Count <= MaxFiles)
            .WithMessage($"Ask for at most {MaxFiles} files at once.")
            .OverridePropertyName("fileNames");
        RuleForEach(x => x)
            .NotEmpty()
            .MaximumLength(1024)
            .OverridePropertyName("fileNames");
    }
}
