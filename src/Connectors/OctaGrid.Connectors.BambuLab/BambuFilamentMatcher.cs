using OctaGrid.Extensions.Models;

namespace OctaGrid.Connectors.BambuLab;

/// <summary>Which spool feeds each filament of a plate, as the start command takes it.</summary>
/// <param name="UseAms">False when the plate prints from the external spool.</param>
/// <param name="AmsMapping">
/// The tray id for each filament of the project, by filament number from 1 at index 0; -1 for filaments the plate
/// does not use. Empty when <paramref name="UseAms"/> is false.
/// </param>
internal sealed record BambuFilamentMapping(bool UseAms, IReadOnlyList<int> AmsMapping);

/// <summary>
/// Matches a plate's filaments to the loaded spools by type and colour. A wrong spool prints the wrong material, so
/// a filament without an exact match stops the start instead of being guessed. Several trays with the same type and
/// colour are interchangeable; the lowest tray id is used.
/// </summary>
internal static class BambuFilamentMatcher
{
    /// <summary>Maps every filament of <paramref name="plate"/>, or says why it cannot.</summary>
    /// <param name="plate">The plate to print.</param>
    /// <param name="loaded">What the printer has loaded.</param>
    /// <param name="mapping">The mapping, when every filament has a spool.</param>
    /// <param name="problem">Why the plate cannot start, written for the user, when it cannot.</param>
    public static bool TryMatch(BambuPlate plate, BambuLoadedFilaments loaded, out BambuFilamentMapping mapping, out string problem)
    {
        mapping = new BambuFilamentMapping(false, []);
        problem = string.Empty;

        if (plate.Filaments.Count == 0)
        {
            problem = "The file does not say which filament the plate uses. Slice it again in Bambu Studio.";
            return false;
        }

        if (!loaded.HasAms)
        {
            return TryExternalOnly(plate, loaded, out mapping, out problem);
        }

        var trays = new int[plate.Filaments.Max(f => f.Id)];
        Array.Fill(trays, -1);
        var missing = new List<BambuPlateFilament>();
        foreach (var filament in plate.Filaments)
        {
            var tray = loaded.AmsTrays
                .Where(t => Matches(filament, t))
                .OrderBy(t => t.TrayId)
                .FirstOrDefault();
            if (tray is null)
            {
                missing.Add(filament);
            }
            else
            {
                trays[filament.Id - 1] = tray.TrayId;
            }
        }

        if (missing.Count == 0)
        {
            mapping = new BambuFilamentMapping(true, trays);
            return true;
        }

        // One filament can come from the external spool instead, as Bambu Studio allows.
        if (plate.Filaments.Count == 1 && loaded.ExternalSpool is { } external && Matches(plate.Filaments[0], external))
        {
            return true;
        }

        problem = missing.Count == 1
            ? $"No loaded spool matches {Describe(missing)}. Load it in the AMS and set its type and colour on the printer."
            : $"No loaded spools match {Describe(missing)}. Load them in the AMS and set their types and colours on the printer.";
        if (loaded.HasAmsHt)
        {
            problem += " OctaGrid cannot print from an AMS HT yet.";
        }

        return false;
    }

    /// <summary>
    /// The plate's filaments for the start dialog, each with the spool it would print from: the one in
    /// <paramref name="mapping"/> when the plate matched, else any tray that matches it alone.
    /// </summary>
    public static IReadOnlyList<PlateFilament> Describe(BambuPlate plate, BambuLoadedFilaments loaded, BambuFilamentMapping? mapping) =>
        plate.Filaments
            .Select(f => new PlateFilament(f.Type, f.Color.Length > 0 ? f.Color : null, SourceOf(f, loaded, mapping)))
            .ToList();

    /// <summary>A tray as Bambu's own apps name it: the AMS unit's letter and the slot, such as "AMS A2".</summary>
    public static string TrayLabel(int trayId) => $"AMS {(char)('A' + trayId / 4)}{trayId % 4 + 1}";

    private const string ExternalSpoolLabel = "External spool";

    private static string? SourceOf(BambuPlateFilament filament, BambuLoadedFilaments loaded, BambuFilamentMapping? mapping)
    {
        if (mapping is not null)
        {
            return mapping.UseAms ? TrayLabel(mapping.AmsMapping[filament.Id - 1]) : ExternalSpoolLabel;
        }

        return loaded.AmsTrays.Where(t => Matches(filament, t)).OrderBy(t => t.TrayId).FirstOrDefault() is { } tray
            ? TrayLabel(tray.TrayId)
            : null;
    }

    private static bool TryExternalOnly(BambuPlate plate, BambuLoadedFilaments loaded, out BambuFilamentMapping mapping, out string problem)
    {
        mapping = new BambuFilamentMapping(false, []);
        problem = string.Empty;
        if (plate.Filaments.Count > 1)
        {
            problem = $"The plate uses {plate.Filaments.Count} filaments, and the printer has no AMS.";
            return false;
        }

        // Without an AMS the printer cannot tell what is loaded. A type set on the external spool is checked; the
        // colour is not, because it is rarely set there.
        var filament = plate.Filaments[0];
        if (loaded.ExternalSpool is { } external && !string.Equals(external.Type, filament.Type, StringComparison.OrdinalIgnoreCase))
        {
            problem = $"The plate prints with {filament.Type}, and the external spool is set to {external.Type}.";
            return false;
        }

        return true;
    }

    private static bool Matches(BambuPlateFilament filament, BambuTray tray) =>
        filament.Type.Length > 0
        && string.Equals(filament.Type, tray.Type, StringComparison.OrdinalIgnoreCase)
        && string.Equals(filament.Color, tray.Color, StringComparison.OrdinalIgnoreCase);

    private static string Describe(IReadOnlyList<BambuPlateFilament> filaments) =>
        string.Join(", ", filaments.Select(f => f.Color.Length > 0 ? $"{f.Type} #{f.Color}" : f.Type));
}
