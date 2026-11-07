namespace OctaGrid.Protocol.Extensions;

/// <summary>The outcome of installing, approving or removing an extension.</summary>
/// <param name="Success">Whether the action was done.</param>
/// <param name="ExtensionId">The extension the action was about, when it is known.</param>
/// <param name="Message">What happened, or why it did not work. Shown to the user.</param>
public sealed record ExtensionInstallResult(bool Success, string? ExtensionId, string Message)
{
    public static ExtensionInstallResult Ok(string? extensionId, string message) => new(true, extensionId, message);

    public static ExtensionInstallResult Fail(string message, string? extensionId = null) => new(false, extensionId, message);
}
