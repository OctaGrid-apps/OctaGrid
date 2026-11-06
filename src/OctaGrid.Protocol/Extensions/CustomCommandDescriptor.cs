namespace OctaGrid.Protocol.Extensions;

/// <summary>How careful the user must be before running a custom command.</summary>
public enum CustomCommandRisk
{
    /// <summary>Read-only or harmless, for example "Home axes".</summary>
    Low,

    /// <summary>Changes printer state, for example "Set bed temperature".</summary>
    Medium,

    /// <summary>Can damage the printer or cause injury, for example raw G-code. Always confirmed.</summary>
    High,
}

/// <summary>A command a connector declares for Expert mode. The agent never runs an undeclared command.</summary>
/// <param name="Id">Stable identifier, unique within the connector.</param>
/// <param name="Name">Text shown on buttons and menus.</param>
/// <param name="Description">What the command does.</param>
/// <param name="Risk">Decides how strongly the UI asks for confirmation.</param>
/// <param name="Parameters">Inputs the user fills in before running the command.</param>
public sealed record CustomCommandDescriptor(
    string Id,
    string Name,
    string? Description,
    CustomCommandRisk Risk,
    IReadOnlyList<SettingField> Parameters);

/// <summary>The outcome of a custom command.</summary>
public readonly record struct CommandResult(bool Success, string? Message = null)
{
    public static CommandResult Ok(string? message = null) => new(true, message);

    public static CommandResult Fail(string message) => new(false, message);
}
