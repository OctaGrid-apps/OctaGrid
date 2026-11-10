namespace OctaGrid.Client.ViewModels;

/// <summary>
/// Sent when something (a dashboard card, later a toast or the printers list) asks for a printer's page.
/// The card is passed along because it already receives the printer's telemetry and owns its commands, so the page
/// shows the same live data without subscribing a second time.
/// </summary>
/// <param name="Card">The card view model of the printer to open.</param>
public sealed record OpenPrinterPageRequested(PrinterDashboardCardViewModel Card);
