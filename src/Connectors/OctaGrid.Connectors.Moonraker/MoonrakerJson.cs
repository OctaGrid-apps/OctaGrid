using System.Text.Json;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Connectors.Moonraker;

internal static class MoonrakerJson
{
    /// <summary>
    /// Combines Moonraker's two independent state machines into the single state the UI shows.
    /// <para>
    /// <c>webhooks.state</c> (ready | startup | shutdown | error) describes the PRINTER — is Klipper
    /// running and connected to the MCU. <c>print_stats.state</c> (standby | printing | paused |
    /// complete | cancelled | error) describes the current or last JOB. Neither stands in for the
    /// other: a job can end in "error" (a G-code error, say) while Klipper stays "ready" and the
    /// printer can start the next print immediately. So the printer level wins, and the job level
    /// only decides what a working printer is doing.
    /// </para>
    /// </summary>
    public static UniversalPrinterState ToPrinterState(string? printStatsState, string? klippyState)
    {
        // 1. Printer level. An unknown (null) klippy state is not a fault; fall through to the job.
        return (klippyState?.ToLowerInvariant()) switch
        {
            "shutdown" or "error" => UniversalPrinterState.Error,
            "startup" => UniversalPrinterState.Offline,// Klipper is (re)connecting to the MCU and rejects commands. Not usable yet, but
                                                       // not a fault — Offline keeps Start/Stop disabled until it reports "ready".
                                                       // 2. Job level. complete/cancelled/error are all "last job result" states that persist in
                                                       // print_stats until the next print starts; the printer itself is ready in each of them.
            _ => printStatsState?.ToLowerInvariant() switch
            {
                "printing" => UniversalPrinterState.Printing,
                "paused" => UniversalPrinterState.Paused,
                "complete" => UniversalPrinterState.Completed,
                "cancelled" => UniversalPrinterState.Cancelled,
                "error" => UniversalPrinterState.Failed,
                _ => UniversalPrinterState.Idle // "standby", or not received yet
            },
        };
    }

    public static bool TryGetDouble(this JsonElement element, string propertyName, out double value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.Number &&
            prop.TryGetDouble(out value))
        {
            return true;
        }
        value = default;
        return false;
    }

    public static bool TryGetRoundDouble(this JsonElement element, int digits, string propertyName, out double value)
    {
        if (TryGetDouble(element, propertyName, out var response))
        {
            value = Math.Round(response, digits);
            return true;
        }
        value = default;
        return false;
    }

    public static bool TryGetString(this JsonElement element, string propertyName, out string? value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return true;
        }
        value = null;
        return false;
    }

    public static bool Update(this JsonElement element, string name, double? current, Action<double> apply, int digits = 1)
    {
        // Only a reported value that differs is a change. (This used to also fire when the property was missing and the
        // current value was 0, and for every reported value, so almost every update was sent on.)
        if (element.TryGetRoundDouble(digits, name, out var value) && value != current)
        {
            apply(value);
            return true;
        }

        return false;
    }

    public static bool Update(this JsonElement element, string name, int? current, Action<int?> apply)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                var value = prop.GetInt32();
                if (value != current)
                {
                    apply(value);
                    return true;
                }
            }
            else
            {
                if (current != null)
                {
                    apply(null);
                    return true;
                }
            }
        }

        return false;
    }

    public static bool Update(this JsonElement element, string name, string? current, Action<string?> apply)
    {
        if (element.TryGetString(name, out var value))
        {
            if (value != current)
            {
                apply(value);
                return true;
            }
        }

        return false;
    }

    public static bool Update(this JsonElement element, string name, TimeOnly current, Action<TimeOnly> apply)
    {
        if (element.TryGetDouble(name, out var value))
        {
            var timeOnly = TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(value));
            if (timeOnly != current)
            {
                apply(timeOnly);
                return true;
            }
        }
        return false;
    }
}
