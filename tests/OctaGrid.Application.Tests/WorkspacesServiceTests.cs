using OctaGrid.Application.Authorization;
using OctaGrid.Application.Services;
using OctaGrid.Application.Telemetry;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Permissions;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Application.Tests;

/// <summary>A workspace is returned with what the asking user may do in it, taken from the role table.</summary>
public sealed class WorkspacesServiceTests : IDisposable
{
    private readonly AgentLinkFixture _fixture = new();
    private readonly RolePermissionPolicy _policy = new();

    public void Dispose() => _fixture.Dispose();

    private static WorkspacesService NewService(OctaGridContext context) =>
        new(context, TestAccess.For(context), new NullPublisher());

    [Theory]
    [InlineData(Role.Reader)]
    [InlineData(Role.Operator)]
    [InlineData(Role.Admin)]
    [InlineData(Role.Owner)]
    public async Task A_workspace_is_listed_with_what_the_members_role_allows(Role role)
    {
        var userId = _fixture.AddMember(role);

        await using var context = _fixture.NewContext();
        var service = NewService(context);
        var listed = Assert.Single(await service.GetList(userId, CancellationToken.None));
        var summary = Assert.Single(await service.GetSummary(userId, CancellationToken.None));

        var expected = _policy.For(role).Order().ToList();
        Assert.Equal(expected, listed.Permissions);
        Assert.Equal(expected, summary.Permissions);
    }

    [Fact]
    public async Task Whoever_creates_a_workspace_may_do_everything_in_it()
    {
        await using var context = _fixture.NewContext();
        var created = await NewService(context).Create(_fixture.UserId, new CreateWorkspaceRequest("Mine"), CancellationToken.None);

        Assert.Equal(Enum.GetValues<Permission>(), created.Permissions);
    }

    [Fact]
    public async Task A_renamed_workspace_comes_back_with_the_users_permissions()
    {
        await using var context = _fixture.NewContext();
        var updated = await NewService(context)
            .Update(_fixture.UserId, _fixture.WorkspaceId, new UpdateWorkspaceRequest("Renamed"), CancellationToken.None);

        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(Enum.GetValues<Permission>(), updated.Permissions);
    }

    private sealed class NullPublisher : IMessagePublisher
    {
        public void Publish(EntityChanged entity)
        {
        }
    }
}
