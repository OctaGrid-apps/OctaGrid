namespace OctaGrid.Protocol.AgentLink;

/// <summary>
/// Why a request failed, in the shape of the app's error family (<c>AppException</c>) so the caller can
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
    IReadOnlyDictionary<string, string>? Errors = null);
