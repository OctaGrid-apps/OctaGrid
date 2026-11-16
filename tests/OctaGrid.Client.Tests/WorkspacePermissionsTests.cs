using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Client.Tests;

public sealed class WorkspacePermissionsTests
{
    private readonly ApplicationState _state = new(new InMemorySettingsStore());

    [Fact]
    public void Nothing_is_withheld_while_the_backend_has_not_said_what_is_allowed()
    {
        var permissions = new WorkspacePermissions(_state);

        Assert.True(permissions.Has(Permission.SendCommands));

        _state.SelectedWorkspace = new Workspace { Id = Guid.NewGuid(), Name = "No permissions reported" };

        Assert.True(permissions.Has(Permission.SendCommands));
    }

    [Fact]
    public void Only_what_the_selected_workspace_grants_is_allowed()
    {
        var permissions = new WorkspacePermissions(_state);

        _state.SelectedWorkspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "As a reader",
            Permissions = [Permission.View],
        };

        Assert.True(permissions.Has(Permission.View));
        Assert.False(permissions.Has(Permission.ControlPrint));
    }

    [Fact]
    public void Selecting_another_workspace_changes_what_is_allowed_and_says_so()
    {
        var permissions = new WorkspacePermissions(_state);
        _state.SelectedWorkspace = new Workspace { Id = Guid.NewGuid(), Name = "As a reader", Permissions = [Permission.View] };
        var changes = 0;
        permissions.Changed += (_, _) => changes++;

        _state.SelectedWorkspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "As an operator",
            Permissions = [Permission.View, Permission.ControlPrint],
        };

        Assert.Equal(1, changes);
        Assert.True(permissions.Has(Permission.ControlPrint));
    }
}
