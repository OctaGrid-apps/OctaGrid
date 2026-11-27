using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>
/// Commands waiting for the printer's answer. The printer answers on the report topic with the command's name and
/// sequence id, a <c>result</c> ("success" or "fail", in either case) and sometimes a <c>reason</c>.
/// </summary>
internal sealed class BambuCommandReplies
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BambuCommandReply>> _pending = new();

    /// <summary>Starts waiting for the answer to <paramref name="command"/>. Call before publishing it.</summary>
    public Task<BambuCommandReply> Expect(BambuCommand command)
    {
        var reply = new TaskCompletionSource<BambuCommandReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[Key(command.Name, command.SequenceId)] = reply;
        return reply.Task;
    }

    /// <summary>Stops waiting for the answer to <paramref name="command"/>.</summary>
    public void Forget(BambuCommand command) => _pending.TryRemove(Key(command.Name, command.SequenceId), out _);

    /// <summary>Completes the command that the "print" object of a report answers, if it answers one.</summary>
    public bool TryComplete(JsonElement print)
    {
        if (print.ValueKind != JsonValueKind.Object
            || !TryGetText(print, "command", out var name)
            || !TryGetText(print, "sequence_id", out var sequenceId)
            || !TryGetText(print, "result", out var result)
            || !_pending.TryRemove(Key(name, sequenceId), out var reply))
        {
            return false;
        }

        TryGetText(print, "reason", out var reason);
        return reply.TrySetResult(new BambuCommandReply(result, reason));
    }

    /// <summary>Ends every wait with <paramref name="error"/>, for example when the connection drops.</summary>
    public void FailAll(Exception error)
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var reply))
            {
                reply.TrySetException(error);
            }
        }
    }

    private static string Key(string name, string sequenceId) => name + "|" + sequenceId;

    // Firmware versions differ in whether they send the sequence id as a string or a number.
    private static bool TryGetText(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        switch (property.ValueKind)
        {
            case JsonValueKind.String:
                value = property.GetString() ?? string.Empty;
                return true;
            case JsonValueKind.Number when property.TryGetInt64(out var number):
                value = number.ToString(CultureInfo.InvariantCulture);
                return true;
            default:
                return false;
        }
    }
}

/// <summary>The printer's answer to a command.</summary>
/// <param name="Result">"success" or "fail", in any case.</param>
/// <param name="Reason">Why the printer refused, when it says.</param>
internal sealed record BambuCommandReply(string Result, string? Reason)
{
    /// <summary>Whether the printer accepted the command.</summary>
    public bool Succeeded => string.Equals(Result, "success", StringComparison.OrdinalIgnoreCase);

    /// <summary>What to tell the user when the printer refused to <paramref name="action"/>.</summary>
    public string RefusalMessage(string action)
    {
        // Firmware with Bambu's authorization control takes control commands only from Bambu's own apps, unless the
        // user turned on Developer Mode, which needs LAN Only mode.
        if (Reason?.Contains("verify failed", StringComparison.OrdinalIgnoreCase) == true)
        {
            return $"The printer refused to {action}: it accepts print commands only from Bambu's own apps. To control it " +
                "from OctaGrid, turn on LAN Only mode and Developer Mode in the printer's network settings.";
        }

        return string.IsNullOrWhiteSpace(Reason) || string.Equals(Reason, Result, StringComparison.OrdinalIgnoreCase)
            ? $"The printer refused to {action}."
            : $"The printer refused to {action}: {Reason}.";
    }
}
