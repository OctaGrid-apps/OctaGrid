using System.Globalization;
using System.Text.Json;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>A loaded spool the printer can feed from.</summary>
/// <param name="TrayId">The global tray id the start command maps filaments to: AMS unit × 4 + slot.</param>
/// <param name="Type">The filament type set for the spool, such as "PLA".</param>
/// <param name="Color">The colour as six upper-case hex digits (RRGGBB).</param>
internal sealed record BambuTray(int TrayId, string Type, string Color);

/// <summary>What the printer has loaded, as its last reports said.</summary>
/// <param name="AmsTrays">The loaded AMS trays, by tray id.</param>
/// <param name="ExternalSpool">The spool on the external holder, when its type is set.</param>
internal sealed record BambuLoadedFilaments(IReadOnlyList<BambuTray> AmsTrays, BambuTray? ExternalSpool)
{
    /// <summary>Nothing reported yet.</summary>
    public static BambuLoadedFilaments None { get; } = new([], null);

    /// <summary>Whether any AMS unit is attached, an AMS HT included.</summary>
    public bool HasAms { get; init; }

    /// <summary>
    /// Whether an AMS HT is attached. Its spools are not in <see cref="AmsTrays"/>, because how the start command
    /// addresses them is not known yet.
    /// </summary>
    public bool HasAmsHt { get; init; }

    /// <summary>
    /// Whether a report has described the AMS yet. Until then the AMS cannot be told apart from no AMS, so nothing
    /// should start.
    /// </summary>
    public bool Known { get; init; }
}

/// <summary>
/// Follows the AMS and external spool through the printer's reports. A full report lists every unit and tray; later
/// reports may carry only the trays that changed, so a tray keeps its spool until a report empties it or the
/// existence bits say it is gone.
/// </summary>
internal sealed class BambuAmsTracker
{
    // The external holder's tray id in the report. The start command does not map to it: it turns the AMS off.
    private const int ExternalTrayId = 254;

    // AMS HT units report ids from 128 with one slot each. How the start command addresses them is not known yet,
    // so their spools are not offered for matching; they are only counted, so the AMS is never turned off as if
    // there were none.
    private const int FirstHtUnitId = 128;

    private readonly SortedDictionary<int, SortedDictionary<int, (string Type, string Color)>> _units = [];

    // Kept for the session: no report is known to say an AMS HT was detached.
    private readonly SortedSet<int> _htUnits = [];
    private (string Type, string Color)? _external;
    private bool _known;

    /// <summary>What is loaded now.</summary>
    public BambuLoadedFilaments Loaded { get; private set; } = BambuLoadedFilaments.None;

    /// <summary>Applies the "print" object of a report.</summary>
    public void Apply(JsonElement print)
    {
        var changed = false;
        if (print.TryGetProperty("ams", out var ams) && ams.ValueKind == JsonValueKind.Object)
        {
            if (ams.TryGetProperty("ams", out var units) && units.ValueKind == JsonValueKind.Array)
            {
                changed |= !_known;
                _known = true;
                foreach (var unit in units.EnumerateArray())
                {
                    changed |= ApplyUnit(unit);
                }
            }

            if (TryGetBits(ams, "ams_exist_bits", out var unitBits))
            {
                // No unit at all also describes the AMS, for printers that then leave the unit list out. Any other
                // value does not: the units' trays are still to come.
                if (unitBits == 0)
                {
                    changed |= !_known;
                    _known = true;
                }

                foreach (var unitId in _units.Keys.Where(id => id < 64 && (unitBits & (1UL << id)) == 0).ToList())
                {
                    changed |= _units.Remove(unitId);
                }
            }

            if (TryGetBits(ams, "tray_exist_bits", out var trayBits))
            {
                foreach (var (unitId, slots) in _units.Where(u => u.Key < 16))
                {
                    foreach (var slot in slots.Keys.Where(s => (trayBits & (1UL << (unitId * 4 + s))) == 0).ToList())
                    {
                        changed |= slots.Remove(slot);
                    }
                }
            }
        }

        if (print.TryGetProperty("vt_tray", out var external) && external.ValueKind == JsonValueKind.Object
            && TryReadSpool(external, out var spool))
        {
            _external = spool;
            changed = true;
        }

        if (changed)
        {
            Loaded = Snapshot();
        }
    }

    private bool ApplyUnit(JsonElement unit)
    {
        if (unit.ValueKind != JsonValueKind.Object || !TryGetInt(unit, "id", out var unitId) || unitId < 0)
        {
            return false;
        }

        if (unitId >= FirstHtUnitId)
        {
            return _htUnits.Add(unitId);
        }

        if (!_units.TryGetValue(unitId, out var slots))
        {
            slots = [];
            _units[unitId] = slots;
        }

        if (!unit.TryGetProperty("tray", out var trays) || trays.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        foreach (var tray in trays.EnumerateArray())
        {
            if (tray.ValueKind != JsonValueKind.Object || !TryGetInt(tray, "id", out var slot) || slot is < 0 or > 3)
            {
                continue;
            }

            if (TryReadSpool(tray, out var spool))
            {
                if (spool is { } loaded)
                {
                    slots[slot] = loaded;
                }
                else
                {
                    slots.Remove(slot);
                }
            }
            else if (tray.EnumerateObject().Count() == 1)
            {
                // A tray with nothing but its id is an empty slot.
                slots.Remove(slot);
            }
        }

        return true;
    }

    private BambuLoadedFilaments Snapshot()
    {
        var trays = _units
            .SelectMany(u => u.Value.Select(s => new BambuTray(u.Key * 4 + s.Key, s.Value.Type, s.Value.Color)))
            .ToList();
        var external = _external is { } spool ? new BambuTray(ExternalTrayId, spool.Type, spool.Color) : null;
        return new BambuLoadedFilaments(trays, external)
        {
            HasAms = _units.Count > 0 || _htUnits.Count > 0,
            HasAmsHt = _htUnits.Count > 0,
            Known = _known,
        };
    }

    // True when the tray says what it holds: a spool, or null for none (an empty type).
    private static bool TryReadSpool(JsonElement tray, out (string Type, string Color)? spool)
    {
        spool = null;
        if (!tray.TryGetProperty("tray_type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var type = typeElement.GetString()?.Trim();
        if (string.IsNullOrEmpty(type))
        {
            return true;
        }

        var color = tray.TryGetProperty("tray_color", out var colorElement) && colorElement.ValueKind == JsonValueKind.String
            ? NormalizeColor(colorElement.GetString())
            : null;
        spool = (type, color ?? string.Empty);
        return true;
    }

    /// <summary>
    /// The RRGGBB part of a colour in upper case: the printer reports RRGGBBAA, Bambu Studio writes #RRGGBB.
    /// Null when it is not a hex colour.
    /// </summary>
    public static string? NormalizeColor(string? color)
    {
        var hex = color?.Trim().TrimStart('#');
        if (hex is null || hex.Length < 6 || !hex[..6].All(char.IsAsciiHexDigit))
        {
            return null;
        }

        return hex[..6].ToUpperInvariant();
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static bool TryGetBits(JsonElement element, string name, out ulong bits)
    {
        bits = 0;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && ulong.TryParse(property.GetString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bits);
    }
}
