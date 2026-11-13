using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink;

namespace OctaGrid.Protocol.Tests;

/// <summary>An error raised on the agent has to reach the caller as the same kind of error.</summary>
public sealed class AgentErrorTests
{
    [Fact]
    public void A_validation_error_comes_back_as_a_validation_exception_with_its_fields()
    {
        var original = new ValidationException(new Dictionary<string, string> { ["command"] = "Unknown command.", ["name"] = "Required." });

        var back = Assert.IsType<ValidationException>(AgentError.From(original).ToException());

        Assert.Equal(original.Errors, back.Errors);
        Assert.Equal(original.Message, back.Message);
    }

    [Fact]
    public void A_not_found_error_keeps_its_message()
    {
        var original = new NotFoundException("Printer", Guid.Empty);

        var back = Assert.IsType<NotFoundException>(AgentError.From(original).ToException());

        Assert.Equal(original.Message, back.Message);
    }

    [Fact]
    public void An_authentication_error_keeps_its_message()
    {
        var original = new AuthenticationException("No user is signed in.");

        var back = Assert.IsType<AuthenticationException>(AgentError.From(original).ToException());

        Assert.Equal("No user is signed in.", back.Message);
    }

    [Fact]
    public void A_kind_of_error_this_side_does_not_know_keeps_what_the_agent_reported()
    {
        var error = new AgentError("/errors/printer-busy", "Printer Busy", "The printer is busy.");

        var back = Assert.IsType<AgentRequestException>(error.ToException());

        Assert.Equal("/errors/printer-busy", back.TypeUri);
        Assert.Equal("Printer Busy", back.Title);
        Assert.Equal("The printer is busy.", back.Message);
        Assert.Same(error, back.Error);
    }

    [Fact]
    public void An_error_survives_another_trip()
    {
        var error = new AgentError("/errors/printer-busy", "Printer Busy", "The printer is busy.");

        Assert.Equal(error, AgentError.From(error.ToException()));
    }
}
