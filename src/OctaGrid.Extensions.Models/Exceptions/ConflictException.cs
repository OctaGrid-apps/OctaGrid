namespace OctaGrid.Extensions.Models.Exceptions;

/// <summary>
/// The change was made against data that someone else changed in the meantime, so it was not saved. Reading the
/// data again and repeating the change usually succeeds. A server maps it to <c>409 Conflict</c>.
/// </summary>
public sealed class ConflictException : AppException
{
    /// <summary>The problem type URI of a conflict.</summary>
    public const string Uri = "/errors/conflict";

    /// <inheritdoc />
    public override string TypeUri => Uri;

    /// <inheritdoc />
    public override string Title => "Conflict";

    /// <summary>Creates a conflict, optionally caused by <paramref name="innerException"/>.</summary>
    public ConflictException(Exception? innerException = null)
        : base("The data was changed by someone else at the same time. Reload it and try again.", innerException!) { }
}
