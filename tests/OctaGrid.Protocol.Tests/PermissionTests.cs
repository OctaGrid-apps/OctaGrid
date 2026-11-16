using System.Text.Json;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Permissions;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Protocol.Tests;

/// <summary>Permissions are sent by name, and a name that has been sent once must keep its meaning.</summary>
public sealed class PermissionTests
{
    [Fact]
    public void The_names_of_permissions_do_not_change()
    {
        string[] expected =
        [
            "View", "ControlPrint", "ManageFiles", "SendCommands", "ManagePrinters",
            "ManageDashboards", "ManageAgents", "ManageExtensions", "ManageWorkspace", "ManageMembers",
        ];

        Assert.Equal(expected, Enum.GetNames<Permission>());
    }

    [Fact]
    public void A_permission_is_sent_as_its_name()
    {
        Permission[] permissions = [Permission.View, Permission.SendCommands];

        var json = JsonSerializer.Serialize(permissions);

        Assert.Equal("""["View","SendCommands"]""", json);
        Assert.Equal(permissions, JsonSerializer.Deserialize<Permission[]>(json));
    }

    [Fact]
    public void A_workspace_carries_the_users_permissions_and_none_when_the_backend_did_not_say()
    {
        var withPermissions = new WorkspaceResponse(Guid.NewGuid(), "Lab", 3, [Permission.View, Permission.ControlPrint]);

        var back = JsonSerializer.Deserialize<WorkspaceResponse>(JsonSerializer.Serialize(withPermissions))!;
        var fromAnOlderBackend = JsonSerializer.Deserialize<WorkspaceResponse>("""{"Id":"00000000-0000-0000-0000-000000000001","Name":"Lab","Version":3}""")!;

        Assert.Equal(withPermissions.Permissions, back.Permissions);
        Assert.Null(fromAnOlderBackend.Permissions);
    }

    [Fact]
    public void Lacking_a_permission_is_a_different_error_from_not_being_a_member()
    {
        var forbidden = new ForbiddenException();

        Assert.Equal("/errors/forbidden", forbidden.TypeUri);
        Assert.NotEqual(new AuthenticationException().TypeUri, forbidden.TypeUri);
    }
}
