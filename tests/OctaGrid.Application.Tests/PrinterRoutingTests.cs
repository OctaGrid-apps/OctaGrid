using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Application.Tests;

/// <summary>
/// A user's request about a printer resolves to the printer's own agent,
/// and only when the user has the required permission.
/// </summary>
public sealed class PrinterRoutingTests : IDisposable
{
    private readonly AgentLinkFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_request_goes_to_the_printers_own_agent()
    {
        var printer = await _fixture.CreatePrinter();

        await using var context = _fixture.NewContext();
        Assert.Equal(_fixture.AgentId, await AgentLinkFixture.Routing(context).AgentOf(_fixture.UserId, printer.Id, Permission.View, CancellationToken.None));
    }

    [Fact]
    public async Task A_printer_without_an_agent_has_nowhere_to_send_a_request()
    {
        var printer = await _fixture.CreatePrinter();
        _fixture.UnassignPrinter(printer.Id);

        await using var context = _fixture.NewContext();
        var routing = AgentLinkFixture.Routing(context);
        await Assert.ThrowsAsync<ValidationException>(() => routing.AgentOf(_fixture.UserId, printer.Id, Permission.View, CancellationToken.None));
        Assert.Null(await routing.AssignedAgentOf(_fixture.UserId, printer.Id, Permission.View, CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_printer_is_not_found()
    {
        await using var context = _fixture.NewContext();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            AgentLinkFixture.Routing(context).AgentOf(_fixture.UserId, Guid.NewGuid(), Permission.View, CancellationToken.None));
    }

    [Fact]
    public async Task A_user_outside_the_workspace_is_refused()
    {
        var printer = await _fixture.CreatePrinter();

        await using var context = _fixture.NewContext();
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            AgentLinkFixture.Routing(context).AgentOf(Guid.NewGuid(), printer.Id, Permission.View, CancellationToken.None));
    }

    [Fact]
    public async Task A_member_without_the_permission_is_refused()
    {
        var printer = await _fixture.CreatePrinter();
        var reader = _fixture.AddMember(Role.Reader);

        await using var context = _fixture.NewContext();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            AgentLinkFixture.Routing(context).AgentOf(reader, printer.Id, Permission.ControlPrint, CancellationToken.None));
    }

    [Fact]
    public async Task A_list_leaves_out_unknown_and_unassigned_printers_instead_of_failing()
    {
        var assigned = await _fixture.CreatePrinter();
        var unassigned = await _fixture.CreatePrinter();
        _fixture.UnassignPrinter(unassigned.Id);

        await using var context = _fixture.NewContext();
        var agents = await AgentLinkFixture.Routing(context)
            .AllowedAgentsOf(_fixture.UserId, [assigned.Id, unassigned.Id, Guid.NewGuid()], Permission.View, CancellationToken.None);

        Assert.Equal(_fixture.AgentId, Assert.Single(agents, a => a.Key == assigned.Id).Value);
        Assert.Single(agents);
    }

    [Fact]
    public async Task A_list_leaves_out_printers_the_user_may_not_use()
    {
        var printer = await _fixture.CreatePrinter();
        var reader = _fixture.AddMember(Role.Reader);

        await using var context = _fixture.NewContext();
        var routing = AgentLinkFixture.Routing(context);

        Assert.Empty(await routing.AllowedAgentsOf(reader, [printer.Id], Permission.ControlPrint, CancellationToken.None));
        Assert.Empty(await routing.AllowedAgentsOf(Guid.NewGuid(), [printer.Id], Permission.View, CancellationToken.None));
        Assert.Single(await routing.AllowedAgentsOf(reader, [printer.Id], Permission.View, CancellationToken.None));
    }
}
