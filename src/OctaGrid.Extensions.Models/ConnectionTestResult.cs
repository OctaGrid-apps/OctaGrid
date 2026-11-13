namespace OctaGrid.Extensions.Models;

/// <summary>
/// The result of IPrinterSession.TestConnectionAsync — a one-shot check run before a printer is
/// saved, so Error carries enough detail (wrong host, timeout, auth rejected) for the dialog to
/// show the user something more useful than pass/fail.
/// </summary>
public readonly record struct ConnectionTestResult(bool Success, string? Error = null)
{
    public static ConnectionTestResult Ok() => new(true);
    public static ConnectionTestResult Fail(string error) => new(false, error);
}
