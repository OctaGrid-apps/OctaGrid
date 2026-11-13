using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Protocol.AgentLink;

/// <summary>
/// A request to an agent failed with a kind of error this side has no type for, for example one added by a
/// newer agent. It keeps what the agent reported, so the user still sees the agent's message.
/// </summary>
public sealed class AgentRequestException : AppException
{
    public AgentRequestException(AgentError error) : base(error.Detail)
    {
        Error = error;
    }

    /// <summary>What the agent reported.</summary>
    public AgentError Error { get; }

    public override string TypeUri => Error.TypeUri;

    public override string Title => Error.Title;
}
