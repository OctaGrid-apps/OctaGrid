namespace OctaGrid.Protocol.Exceptions;

public sealed class ValidationException : AppException
{
    public const string Uri = "/errors/validation";
    public override string TypeUri => Uri;
    public override string Title => "Validation Error";

    public IReadOnlyDictionary<string, string> Errors { get; }

    public ValidationException(string field, string message)
        : base(message)
    {
        Errors = new Dictionary<string, string> { [field] = message };
    }

    public ValidationException(IReadOnlyDictionary<string, string> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }
}
