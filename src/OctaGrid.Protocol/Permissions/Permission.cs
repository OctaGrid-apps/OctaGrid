using System.Text.Json.Serialization;

namespace OctaGrid.Protocol.Permissions;

/// <summary>
/// Something a user may do in a workspace. Code asks for a permission and never for a role: which roles have
/// which permissions is decided in one place on the backend. Sent as its name. New values go at the end.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Permission>))]
public enum Permission
{
    /// <summary>Read the workspace: printers, telemetry, file lists, thumbnails, events and console output.</summary>
    View,

    /// <summary>Start, pause, resume and stop a print.</summary>
    ControlPrint,

    /// <summary>Upload files to a printer and delete them from it.</summary>
    ManageFiles,

    /// <summary>Send console commands to a printer.</summary>
    SendCommands,

    /// <summary>Add, change, turn on or off and remove printers, assign them to an agent and test a connection.</summary>
    ManagePrinters,

    /// <summary>Add, change and remove dashboards and choose the default one.</summary>
    ManageDashboards,

    /// <summary>Add and change agents.</summary>
    ManageAgents,

    /// <summary>Install, approve, turn on or off and remove an agent's extensions.</summary>
    ManageExtensions,

    /// <summary>Change the workspace itself, such as its name.</summary>
    ManageWorkspace,

    /// <summary>Add and remove members of the workspace and change their roles.</summary>
    ManageMembers,
}
