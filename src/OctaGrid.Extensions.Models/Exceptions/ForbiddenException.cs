namespace OctaGrid.Extensions.Models.Exceptions;

/// <summary>
/// The user is known and is a member of the workspace, but may not do this there. Different from
/// <see cref="AuthenticationException"/>, which means the user is not known or not a member at all.
/// </summary>
public sealed class ForbiddenException : AppException
{
    public const string Uri = "/errors/forbidden";
    public override string TypeUri => Uri;
    public override string Title => "Forbidden";

    public ForbiddenException(string detail = "You do not have permission to do this.") : base(detail) { }
}
