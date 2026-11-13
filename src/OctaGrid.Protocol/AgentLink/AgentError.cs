using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Protocol.AgentLink;

/// <summary>
/// Why a request failed, in the shape of the app's error family (<see cref="AppException"/>) so the caller can
/// raise the same error the agent did. It maps onto RFC 9457 problem details.
/// </summary>
/// <param name="TypeUri">Identifies the kind of error, for example <c>/errors/validation</c>.</param>
/// <param name="Title">Short name of the kind of error.</param>
/// <param name="Detail">What went wrong, written for the user.</param>
/// <param name="Errors">For a validation error: the message per field. Null otherwise.</param>
public sealed record AgentError(
    string TypeUri,
    string Title,
    string Detail,
    IReadOnlyDictionary<string, string>? Errors = null)
{
    /// <summary>Describes an error the agent raised, so it can be sent back.</summary>
    public static AgentError From(AppException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new AgentError(
            exception.TypeUri,
            exception.Title,
            exception.Message,
            exception is ValidationException validation ? validation.Errors : null);
    }

    /// <summary>
    /// Turns the error back into an exception for the caller. The kinds this side knows come back as their own
    /// type, so code that handles a <see cref="ValidationException"/> behaves the same whether the agent runs
    /// in process or far away. Any other kind comes back as an <see cref="AgentRequestException"/>.
    /// </summary>
    public AppException ToException() => TypeUri switch
    {
        ValidationException.Uri when Errors is { Count: > 0 } => new ValidationException(Errors),
        AuthenticationException.Uri => new AuthenticationException(Detail),
        NotFoundException.Uri => new NotFoundException(Detail),
        _ => new AgentRequestException(this),
    };
}
