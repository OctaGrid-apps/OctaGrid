namespace OctaGrid.Extensions.Models.Exceptions;

/// <summary>
/// The printer's file storage is unavailable, for example because the printer has no SD card.
/// The detail is the user-facing reason. A server maps it to <c>409 Conflict</c>.
/// </summary>
public sealed class StorageUnavailableException : AppException
{
    /// <summary>The problem type URI of an unavailable storage.</summary>
    public const string Uri = "/errors/storage-unavailable";

    /// <inheritdoc />
    public override string TypeUri => Uri;

    /// <inheritdoc />
    public override string Title => "Storage unavailable";

    /// <summary>Creates the error with a user-facing <paramref name="detail"/>.</summary>
    public StorageUnavailableException(string detail)
        : base(detail) { }
}
