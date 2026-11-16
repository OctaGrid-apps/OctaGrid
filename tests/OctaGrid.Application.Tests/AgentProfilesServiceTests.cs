using OctaGrid.Application.Services;
using OctaGrid.Protocol.AgentLink.Requests;

namespace OctaGrid.Application.Tests;

public sealed class AgentProfilesServiceTests : IDisposable
{
    private readonly AgentLinkFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task An_agent_is_sent_its_enabled_printers_with_their_secrets()
    {
        var enabled = await _fixture.CreatePrinter();
        await _fixture.CreatePrinter(enabled: false);
        var unassigned = await _fixture.CreatePrinter();
        _fixture.UnassignPrinter(unassigned.Id);

        await using var context = _fixture.NewContext();
        await new AgentProfilesService(_fixture.Printers(context), _fixture.Link).Push(_fixture.AgentId, CancellationToken.None);

        var (agentId, request) = Assert.Single(_fixture.Link.Sent);
        Assert.Equal(_fixture.AgentId, agentId);
        var profile = Assert.Single(Assert.IsType<ApplyPrinterProfilesRequest>(request).Profiles);
        Assert.Equal(enabled.Id, profile.PrinterId);
        Assert.Equal("123456", profile.Get("apiKey"));
    }

    [Fact]
    public async Task An_agent_without_printers_is_sent_an_empty_set_so_it_lets_go_of_the_old_ones()
    {
        await using var context = _fixture.NewContext();
        await new AgentProfilesService(_fixture.Printers(context), _fixture.Link).Push(_fixture.AgentId, CancellationToken.None);

        Assert.Empty(Assert.IsType<ApplyPrinterProfilesRequest>(Assert.Single(_fixture.Link.Sent).Request).Profiles);
    }
}
