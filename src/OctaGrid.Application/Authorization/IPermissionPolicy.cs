using OctaGrid.Data.Models;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Authorization;

/// <summary>
/// Decides what a role may do. The only thing that knows roles: services ask whether a permission is granted
/// and never look at the role themselves, so changing a role's privileges changes nothing else.
/// </summary>
public interface IPermissionPolicy
{
    /// <summary>The permissions a member with this role has. Empty for a role the policy does not know.</summary>
    IReadOnlySet<Permission> For(Role role);
}
