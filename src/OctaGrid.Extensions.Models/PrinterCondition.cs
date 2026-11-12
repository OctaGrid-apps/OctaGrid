namespace OctaGrid.Extensions.Models;

/// <summary>
/// Something that is wrong with a printer right now, for example "cannot connect" or "Klipper has shut
/// down". Conditions are state, not history: every <see cref="PrinterTelemetry"/> carries the complete
/// current set, and a condition that no longer applies is simply absent. The host records when one
/// appears or disappears as a printer event.
/// </summary>
/// <param name="Code">
/// A stable, machine-readable identifier such as <c>connection.failed</c>. It identifies the condition across
/// snapshots, so a connector must reuse the same code while the same problem persists.
/// </param>
/// <param name="Severity">How serious the condition is.</param>
/// <param name="Message">A short sentence for the user.</param>
public record PrinterCondition(string Code, PrinterSeverity Severity, string Message);
