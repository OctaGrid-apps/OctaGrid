namespace OctaGrid.Extensions.Models.Exceptions;

/// <summary>
/// The printer's file storage cannot be reached right now, for example because it has no SD card. The detail says why,
/// written for the user. A server maps it to <c>409 Conflict</c>: the printer's state does not allow the request.
/// </summary>
public sealed class StorageUnavailableException : AppException
{
    /// <summary>The problem type URI of an unavailable storage.</summary>
    public const string Uri = "/errors/storage-unavailable";

    /// <inheritdoc />
    public override string TypeUri => Uri;

    /// <inheritdoc />
    public override string Title => "Storage unavailable";

    /// <summary>Creates the error with <paramref name="detail"/>, the reason shown to the user.</summary>
    public StorageUnavailableException(string detail)
        : base(detail) { }
}
