using OctaGrid.Application.Authorization;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Tests;

public sealed class WorkspaceAccessTests : IDisposable
{
    private readonly AgentLinkFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_member_whose_role_has_the_permission_is_let_through()
    {
        var operatorId = _fixture.AddMember(Role.Operator);

        await using var context = _fixture.NewContext();
        await TestAccess.For(context).Require(operatorId, _fixture.WorkspaceId, Permission.ControlPrint, CancellationToken.None);
    }

    [Fact]
    public async Task A_member_whose_role_lacks_the_permission_is_forbidden()
    {
        var readerId = _fixture.AddMember(Role.Reader);

        await using var context = _fixture.NewContext();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => TestAccess.For(context).Require(readerId, _fixture.WorkspaceId, Permission.ControlPrint, CancellationToken.None));
    }

    [Fact]
    public async Task Someone_who_is_not_a_member_is_refused_whatever_the_permission()
    {
        await using var context = _fixture.NewContext();
        await Assert.ThrowsAsync<AuthenticationException>(
            () => TestAccess.For(context).Require(Guid.NewGuid(), _fixture.WorkspaceId, Permission.View, CancellationToken.None));
    }

    [Fact]
    public async Task An_agent_is_checked_through_its_workspace()
    {
        var readerId = _fixture.AddMember(Role.Reader);
        var foreignAgent = _fixture.AddForeignAgent();

        await using var context = _fixture.NewContext();
        var access = TestAccess.For(context);

        await access.RequireForAgent(readerId, _fixture.AgentId, Permission.View, CancellationToken.None);
        await Assert.ThrowsAsync<ForbiddenException>(
            () => access.RequireForAgent(readerId, _fixture.AgentId, Permission.ManageExtensions, CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(
            () => access.RequireForAgent(readerId, foreignAgent, Permission.View, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(
            () => access.RequireForAgent(readerId, Guid.NewGuid(), Permission.View, CancellationToken.None));
    }

    [Fact]
    public async Task What_a_role_may_do_comes_from_the_policy_alone()
    {
        var readerId = _fixture.AddMember(Role.Reader);

        await using var context = _fixture.NewContext();
        var generous = new WorkspaceAccess(context, new EveryoneMayDoEverything());

        await generous.Require(readerId, _fixture.WorkspaceId, Permission.ManageMembers, CancellationToken.None);
    }

    private sealed class EveryoneMayDoEverything : IPermissionPolicy
    {
        public IReadOnlySet<Permission> For(Role role) => Enum.GetValues<Permission>().ToHashSet();
    }
}
