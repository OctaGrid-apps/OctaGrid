using System.Collections.Frozen;
using OctaGrid.Data.Models;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Authorization;

/// <summary>The built-in roles and their fixed permissions (<see cref="RolePermissions"/>).</summary>
public sealed class RolePermissionPolicy : IPermissionPolicy
{
    public IReadOnlySet<Permission> For(Role role) =>
        RolePermissions.ByRole.TryGetValue(role, out var permissions) ? permissions : FrozenSet<Permission>.Empty;
}
