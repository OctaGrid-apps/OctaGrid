using OctaGrid.Application.Services;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink.Requests;

namespace OctaGrid.Application.Tests;

public sealed class ExtensionsServiceTests : IDisposable
{
    private readonly AgentLinkFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_change_goes_to_the_agent_that_is_named()
    {
        await using var context = _fixture.NewContext();
        await new ExtensionsService(context, _fixture.Link)
            .SetEnabled(_fixture.UserId, _fixture.AgentId, "acme.printer", enabled: false, CancellationToken.None);

        var (agentId, request) = Assert.Single(_fixture.Link.Sent);
        Assert.Equal(_fixture.AgentId, agentId);
        Assert.Equal(new SetExtensionEnabledRequest("acme.printer", false), request);
    }

    [Fact]
    public async Task A_package_is_uploaded_to_the_agent_and_then_installed()
    {
        await using var context = _fixture.NewContext();
        var result = await new ExtensionsService(context, _fixture.Link)
            .Install(_fixture.UserId, _fixture.AgentId, new MemoryStream([1, 2, 3, 4, 5]), CancellationToken.None);

        Assert.True(result.Success);
        Assert.All(_fixture.Link.Sent, sent => Assert.Equal(_fixture.AgentId, sent.AgentId));
        var requests = _fixture.Link.Sent.Select(s => s.Request).ToList();
        Assert.IsType<BeginUploadRequest>(requests[0]);
        Assert.Equal([1, 2, 3, 4, 5], requests.OfType<UploadChunkRequest>().SelectMany(c => c.Data));
        Assert.Equal(new InstallExtensionRequest(_fixture.Link.UploadId), requests[^1]);
    }

    [Fact]
    public async Task A_user_outside_the_agents_workspace_cannot_see_or_change_its_extensions()
    {
        var foreignAgent = _fixture.AddForeignAgent();

        await using var context = _fixture.NewContext();
        var service = new ExtensionsService(context, _fixture.Link);
        var user = _fixture.UserId;

        await Assert.ThrowsAsync<AuthenticationException>(() => service.GetList(user, foreignAgent, CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(() => service.SetEnabled(user, foreignAgent, "acme.printer", true, CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(() => service.Install(user, foreignAgent, new MemoryStream([1]), CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(() => service.Approve(user, foreignAgent, "acme.printer", CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(() => service.Uninstall(user, foreignAgent, "acme.printer", CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(() => service.GetDeveloperFolder(user, foreignAgent, CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(() => service.SetDeveloperFolder(user, foreignAgent, "C:/dev", CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(() => service.ReloadDeveloperExtensions(user, foreignAgent, CancellationToken.None));
        Assert.Empty(_fixture.Link.Sent);
    }

    [Fact]
    public async Task An_unknown_agent_is_not_found()
    {
        await using var context = _fixture.NewContext();
        await Assert.ThrowsAsync<NotFoundException>(
            () => new ExtensionsService(context, _fixture.Link).GetList(_fixture.UserId, Guid.NewGuid(), CancellationToken.None));
        Assert.Empty(_fixture.Link.Sent);
    }
}
