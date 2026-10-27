namespace OctaGrid.Protocol.Exceptions;

public sealed class AuthenticationException : AppException
{
    public const string Uri = "/errors/authentication";
    public override string TypeUri => Uri;
    public override string Title => "Authentication Failed";

    // Deliberately generic — never reveal whether the email or the password was wrong.
    public AuthenticationException(string detail = "Invalid email or password.") : base(detail) { }
}
