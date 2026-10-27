namespace OctaGrid.Protocol.Exceptions;

public abstract class AppException : Exception
{
    public abstract string TypeUri { get; }
    public abstract string Title { get; }

    protected AppException(string detail) : base(detail) { }
    protected AppException(string detail, Exception innerException) : base(detail, innerException) { }
}
