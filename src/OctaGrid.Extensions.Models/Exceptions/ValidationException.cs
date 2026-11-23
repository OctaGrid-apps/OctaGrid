namespace OctaGrid.Extensions.Models.Exceptions;

/// <summary>The input was rejected. Carries the messages per field, as an HTTP problem details response lists them.</summary>
public sealed class ValidationException : AppException
{
    /// <summary>The problem type of every validation failure.</summary>
    public const string Uri = "/errors/validation";

    /// <inheritdoc />
    public override string TypeUri => Uri;

    /// <inheritdoc />
    public override string Title => "Validation Error";

    /// <summary>The first message of each field that failed, keyed by field name.</summary>
    public IReadOnlyDictionary<string, string> Errors { get; }

    /// <summary>Every message of each field that failed, keyed by field name. A field has at least one.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Failures { get; }

    /// <summary>One field failed with one message.</summary>
    public ValidationException(string field, string message)
        : base(message)
    {
        Errors = new Dictionary<string, string> { [field] = message };
        Failures = new Dictionary<string, IReadOnlyList<string>> { [field] = [message] };
    }

    /// <summary>Several fields failed, with one message each.</summary>
    public ValidationException(IReadOnlyDictionary<string, string> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
        Failures = errors.ToDictionary(e => e.Key, e => (IReadOnlyList<string>)[e.Value]);
    }

    /// <summary>Several fields failed, each with one or more messages.</summary>
    public ValidationException(IReadOnlyDictionary<string, IReadOnlyList<string>> failures)
        : base("One or more validation errors occurred.")
    {
        Failures = failures;
        Errors = failures
            .Where(f => f.Value.Count > 0)
            .ToDictionary(f => f.Key, f => f.Value[0]);
    }
}
