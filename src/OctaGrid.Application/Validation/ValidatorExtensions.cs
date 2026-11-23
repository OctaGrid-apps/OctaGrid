using FluentValidation;
using ValidationException = OctaGrid.Extensions.Models.Exceptions.ValidationException;

namespace OctaGrid.Application.Validation;

internal static class ValidatorExtensions
{
    /// <summary>Throws a <see cref="ValidationException"/> with every message, per field, when the request is invalid.</summary>
    public static void ValidateOrThrow<T>(this IValidator<T> validator, T request)
    {
        var result = validator.Validate(request);
        if (result.IsValid)
        {
            return;
        }

        var failures = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(e => e.ErrorMessage).ToList());
        throw new ValidationException(failures);
    }
}
