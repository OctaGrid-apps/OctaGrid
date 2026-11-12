using OctaGrid.Extensions.Models;

namespace OctaGrid.Extensions.Abstractions.Tests;

public class ConsoleCommandRulesTests
{
    [Theory]
    [InlineData("M105", "M105")]
    [InlineData("  G1 X10 Y10 F3000  ", "G1 X10 Y10 F3000")]
    [InlineData("SET_HEATER_TEMPERATURE HEATER=extruder TARGET=210", "SET_HEATER_TEMPERATURE HEATER=extruder TARGET=210")]
    [InlineData("M117 Hello wörld", "M117 Hello wörld")]
    public void A_single_line_of_plain_text_is_accepted_without_the_spaces_around_it(string input, string expected)
    {
        Assert.True(ConsoleCommandRules.TryNormalize(input, out var command, out var error));
        Assert.Equal(expected, command);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Nothing_to_send_is_refused(string? input)
    {
        Assert.False(ConsoleCommandRules.TryNormalize(input, out var command, out var error));
        Assert.Equal(string.Empty, command);
        Assert.Equal("Type a command first.", error);
    }

    [Theory]
    [InlineData("M105\nM104 S0")]
    [InlineData("M105\r\nM104 S0")]
    [InlineData("M105\u0000")]
    [InlineData("M105\u001b[2J")]
    [InlineData("M105\tS1")]
    [InlineData("M105\u2028M104 S0")]
    [InlineData("M105\u2029M104 S0")]
    [InlineData("M105\u0085M104 S0")]
    public void Several_lines_and_control_characters_are_refused(string input)
    {
        Assert.False(ConsoleCommandRules.TryNormalize(input, out var command, out var error));
        Assert.Equal(string.Empty, command);
        Assert.Equal("A command must be a single line of plain text.", error);
    }

    [Fact]
    public void A_command_at_the_limit_is_accepted_and_one_character_more_is_refused()
    {
        Assert.True(ConsoleCommandRules.TryNormalize(new string('x', ConsoleCommandRules.MaxLength), out _, out _));

        Assert.False(ConsoleCommandRules.TryNormalize(new string('x', ConsoleCommandRules.MaxLength + 1), out _, out var error));
        Assert.Contains(ConsoleCommandRules.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture), error);
    }

    [Fact]
    public void The_limit_counts_what_is_sent_and_not_the_spaces_around_it()
    {
        var input = "  " + new string('x', ConsoleCommandRules.MaxLength) + "  ";

        Assert.True(ConsoleCommandRules.TryNormalize(input, out var command, out _));
        Assert.Equal(ConsoleCommandRules.MaxLength, command.Length);
    }
}
