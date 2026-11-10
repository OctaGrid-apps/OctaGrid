using System.Globalization;

namespace OctaGrid.Protocol.Printers;

/// <summary>
/// What a command typed into a printer's console may look like. The same rules are applied where the user types it and
/// again by the agent before anything reaches a printer, so a client that skips them cannot send more than a typed line.
/// A command is one line: a printer runs each line on its own, so one line is one thing the user can read and confirm.
/// </summary>
public static class ConsoleCommandRules
{
    /// <summary>The most characters a command may have. Real G-code lines are far shorter.</summary>
    public const int MaxLength = 256;

    /// <summary>
    /// Checks a typed command and returns it ready to send: without spaces around it.
    /// </summary>
    /// <param name="input">What the user typed.</param>
    /// <param name="command">The command to send; empty when the input is not acceptable.</param>
    /// <param name="error">Why the input is not acceptable, in words for the user; null when it is.</param>
    /// <returns>True when the command can be sent.</returns>
    public static bool TryNormalize(string? input, out string command, out string? error)
    {
        command = string.Empty;
        var trimmed = input?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            error = "Type a command first.";
            return false;
        }

        if (trimmed.Length > MaxLength)
        {
            error = $"A command can have at most {MaxLength} characters.";
            return false;
        }

        foreach (var c in trimmed)
        {
            if (IsLineBreakOrControl(c))
            {
                error = "A command must be a single line of plain text.";
                return false;
            }
        }

        command = trimmed;
        error = null;
        return true;
    }

    /// <summary>
    /// True for a control character, and for the Unicode line and paragraph separators (U+2028, U+2029), which are not
    /// control characters but still end a line for some software.
    /// </summary>
    public static bool IsLineBreakOrControl(char c) =>
        char.IsControl(c)
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
