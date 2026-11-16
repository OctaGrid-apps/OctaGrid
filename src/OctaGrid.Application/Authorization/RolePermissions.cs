using System.Collections.Frozen;
using OctaGrid.Data.Models;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Authorization;

/// <summary>
/// What each role may do. This is the one place to edit when a role's privileges change; a new role needs a
/// value in <see cref="Role"/> and a row here.
/// </summary>
internal static class RolePermissions
{
    private static readonly Permission[] Reader =
    [
        Permission.View,
    ];

    private static readonly Permission[] Operator =
    [
        .. Reader,
        Permission.ControlPrint,
        Permission.ManageFiles,
    ];

    private static readonly Permission[] Admin =
    [
        .. Operator,
        Permission.SendCommands,
        Permission.ManagePrinters,
        Permission.ManageDashboards,
        Permission.ManageAgents,
        Permission.ManageExtensions,
    ];

    private static readonly Permission[] Owner =
    [
        .. Admin,
        Permission.ManageWorkspace,
        Permission.ManageMembers,
    ];

    public static FrozenDictionary<Role, FrozenSet<Permission>> ByRole { get; } = new Dictionary<Role, FrozenSet<Permission>>
    {
        [Role.Reader] = Reader.ToFrozenSet(),
        [Role.Operator] = Operator.ToFrozenSet(),
        [Role.Admin] = Admin.ToFrozenSet(),
        [Role.Owner] = Owner.ToFrozenSet(),
    }.ToFrozenDictionary();
}
