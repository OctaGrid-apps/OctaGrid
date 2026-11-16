using OctaGrid.Application.Authorization;
using OctaGrid.Data;

namespace OctaGrid.Application.Tests;

internal static class TestAccess
{
    /// <summary>The access check services are given, with the real roles and their permissions.</summary>
    public static WorkspaceAccess For(OctaGridContext context) => new(context, new RolePermissionPolicy());
}
