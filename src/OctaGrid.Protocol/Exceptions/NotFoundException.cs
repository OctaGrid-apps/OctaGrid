namespace OctaGrid.Protocol.Exceptions;

public sealed class NotFoundException : AppException
{
    public const string Uri = "/errors/not-found";
    public override string TypeUri => Uri;
    public override string Title => "Not Found";

    public NotFoundException(string entity, object key)
        : base($"{entity} with key '{key}' was not found.") { }

    public NotFoundException(string detail) : base(detail)
    { }
}
