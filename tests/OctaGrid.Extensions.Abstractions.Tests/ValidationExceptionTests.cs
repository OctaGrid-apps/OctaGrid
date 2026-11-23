using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Extensions.Abstractions.Tests;

public sealed class ValidationExceptionTests
{
    [Fact]
    public void Every_message_of_a_field_is_kept_and_the_first_one_is_its_error()
    {
        var exception = new ValidationException(new Dictionary<string, IReadOnlyList<string>>
        {
            ["name"] = ["Required.", "Too long."],
            ["url"] = ["Not a URL."],
        });

        Assert.Equal(["Required.", "Too long."], exception.Failures["name"]);
        Assert.Equal("Required.", exception.Errors["name"]);
        Assert.Equal("Not a URL.", exception.Errors["url"]);
    }

    [Fact]
    public void One_message_per_field_is_also_listed_as_its_failures()
    {
        var exception = new ValidationException(new Dictionary<string, string> { ["name"] = "Required." });

        Assert.Equal(["Required."], exception.Failures["name"]);
    }
}
