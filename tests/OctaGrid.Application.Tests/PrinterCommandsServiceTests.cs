using OctaGrid.Application.Services;
using OctaGrid.Data;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink.Requests;

namespace OctaGrid.Application.Tests;

public sealed class PrinterCommandsServiceTests : IDisposable
{
    private readonly AgentLinkFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private PrinterCommandsService NewService(OctaGridContext context) =>
        new(TestAccess.For(context), AgentLinkFixture.Routing(context), _fixture.Secrets(context), _fixture.Link);

    [Fact]
    public async Task A_command_goes_to_the_printers_agent()
    {
        var printer = await _fixture.CreatePrinter();

        await using var context = _fixture.NewContext();
        await NewService(context).StartPrint(_fixture.UserId, printer.Id, "benchy.gcode", CancellationToken.None);

        var (agentId, request) = Assert.Single(_fixture.Link.Sent);
        Assert.Equal(_fixture.AgentId, agentId);
        Assert.Equal(new StartPrintRequest(printer.Id, "benchy.gcode"), request);
    }

    [Fact]
    public async Task A_user_outside_the_workspace_cannot_command_a_printer()
    {
        var printer = await _fixture.CreatePrinter();

        await using var context = _fixture.NewContext();
        var service = NewService(context);
        var stranger = Guid.NewGuid();

        await Assert.ThrowsAsync<AuthenticationException>(() => service.StopPrint(stranger, printer.Id, CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(() => service.SendConsoleCommand(stranger, printer.Id, "M112", CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(() => service.GetFiles(stranger, printer.Id, CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(
            () => service.UploadFile(stranger, printer.Id, new MemoryStream([1, 2, 3]), "a.gcode", CancellationToken.None));
        Assert.Empty(_fixture.Link.Sent);
    }

    [Fact]
    public async Task File_details_are_asked_of_the_printers_agent_for_the_named_files()
    {
        var printer = await _fixture.CreatePrinter();

        await using var context = _fixture.NewContext();
        await NewService(context).GetFileDetails(_fixture.UserId, printer.Id, ["a.gcode.3mf", "b.gcode.3mf"], CancellationToken.None);

        var (agentId, request) = Assert.Single(_fixture.Link.Sent);
        Assert.Equal(_fixture.AgentId, agentId);
        var details = Assert.IsType<GetPrinterFileDetailsRequest>(request);
        Assert.Equal(["a.gcode.3mf", "b.gcode.3mf"], details.FileNames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task File_details_are_asked_for_at_least_one_and_at_most_fifty_files(int count)
    {
        var printer = await _fixture.CreatePrinter();
        var names = Enumerable.Range(0, count).Select(i => $"{i}.gcode.3mf").ToList();

        await using var context = _fixture.NewContext();
        await Assert.ThrowsAsync<ValidationException>(
            () => NewService(context).GetFileDetails(_fixture.UserId, printer.Id, names, CancellationToken.None));
        Assert.Empty(_fixture.Link.Sent);
    }

    [Fact]
    public async Task A_printer_without_an_agent_cannot_be_commanded()
    {
        var printer = await _fixture.CreatePrinter();
        _fixture.UnassignPrinter(printer.Id);

        await using var context = _fixture.NewContext();
        await Assert.ThrowsAsync<ValidationException>(
            () => NewService(context).PausePrint(_fixture.UserId, printer.Id, CancellationToken.None));
        Assert.Empty(_fixture.Link.Sent);
    }

    [Fact]
    public async Task An_uploaded_file_is_sent_in_pieces_and_then_stored_on_the_printer()
    {
        var printer = await _fixture.CreatePrinter();

        await using var context = _fixture.NewContext();
        await NewService(context).UploadFile(
            _fixture.UserId, printer.Id, new MemoryStream([1, 2, 3, 4, 5, 6]), "a.gcode", CancellationToken.None);

        Assert.All(_fixture.Link.Sent, sent => Assert.Equal(_fixture.AgentId, sent.AgentId));
        var requests = _fixture.Link.Sent.Select(s => s.Request).ToList();
        Assert.IsType<BeginUploadRequest>(requests[0]);
        var chunks = requests.OfType<UploadChunkRequest>().ToList();
        Assert.Equal([1, 2, 3, 4, 5, 6], chunks.SelectMany(c => c.Data));
        Assert.Equal(new StorePrinterFileRequest(printer.Id, _fixture.Link.UploadId, "a.gcode"), requests[^1]);
    }

    [Fact]
    public async Task A_saved_printer_is_tested_by_its_own_agent_with_its_saved_secrets()
    {
        var printer = await _fixture.CreatePrinter();
        var foreignAgent = _fixture.AddForeignAgent();

        await using var context = _fixture.NewContext();
        var result = await NewService(context).TestConnection(
            _fixture.UserId,
            _fixture.WorkspaceId,
            "octagrid.bambu-mqtt",
            new Dictionary<string, string> { ["url"] = "192.168.1.9" },
            printer.Id,
            foreignAgent,
            CancellationToken.None);

        Assert.True(result.Success);
        var (agentId, request) = Assert.Single(_fixture.Link.Sent);
        Assert.Equal(_fixture.AgentId, agentId);
        var settings = Assert.IsType<TestConnectionRequest>(request).Settings;
        Assert.Equal("192.168.1.9", settings["url"]);
        Assert.Equal("123456", settings["apiKey"]);
    }

    [Fact]
    public async Task A_saved_printer_cannot_be_tested_by_a_user_outside_its_workspace()
    {
        var printer = await _fixture.CreatePrinter();

        await using var context = _fixture.NewContext();
        await Assert.ThrowsAsync<AuthenticationException>(() => NewService(context).TestConnection(
            Guid.NewGuid(),
            _fixture.WorkspaceId,
            "octagrid.bambu-mqtt",
            new Dictionary<string, string>(),
            printer.Id,
            agentId: null,
            CancellationToken.None));

        Assert.Empty(_fixture.Link.Sent);
    }

    [Fact]
    public async Task A_new_printer_is_tested_by_the_agent_that_is_named()
    {
        var second = _fixture.AddAgent();

        await using var context = _fixture.NewContext();
        await NewService(context).TestConnection(
            _fixture.UserId,
            _fixture.WorkspaceId,
            "octagrid.bambu-mqtt",
            new Dictionary<string, string> { ["url"] = "192.168.1.9" },
            printerId: null,
            second,
            CancellationToken.None);

        Assert.Equal(second, Assert.Single(_fixture.Link.Sent).AgentId);
    }

    [Fact]
    public async Task A_new_printer_without_a_named_agent_is_tested_by_the_only_agent_of_its_workspace()
    {
        await using var context = _fixture.NewContext();
        await NewService(context).TestConnection(
            _fixture.UserId,
            _fixture.WorkspaceId,
            "octagrid.bambu-mqtt",
            new Dictionary<string, string> { ["url"] = "192.168.1.9" },
            printerId: null,
            agentId: null,
            CancellationToken.None);

        Assert.Equal(_fixture.AgentId, Assert.Single(_fixture.Link.Sent).AgentId);
    }

    [Fact]
    public async Task A_new_printer_cannot_be_tested_without_an_agent_or_on_an_agent_the_user_cannot_use()
    {
        _fixture.AddAgent();
        var foreignAgent = _fixture.AddForeignAgent();
        var settings = new Dictionary<string, string> { ["url"] = "192.168.1.9" };
        var workspaceId = _fixture.WorkspaceId;

        await using var context = _fixture.NewContext();
        var service = NewService(context);

        await Assert.ThrowsAsync<ValidationException>(
            () => service.TestConnection(_fixture.UserId, workspaceId, "octagrid.bambu-mqtt", settings, null, null, CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(
            () => service.TestConnection(_fixture.UserId, workspaceId, "octagrid.bambu-mqtt", settings, null, foreignAgent, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(
            () => service.TestConnection(_fixture.UserId, workspaceId, "octagrid.bambu-mqtt", settings, null, Guid.NewGuid(), CancellationToken.None));
        Assert.Empty(_fixture.Link.Sent);
    }

    [Fact]
    public async Task A_new_printer_cannot_be_tested_in_a_workspace_the_user_is_not_a_member_of()
    {
        var foreignAgent = _fixture.AddForeignAgent();
        await using var context = _fixture.NewContext();
        var foreignWorkspace = context.Agents.Single(a => a.Id == foreignAgent).WorkspaceId;

        await Assert.ThrowsAsync<AuthenticationException>(() => NewService(context).TestConnection(
            _fixture.UserId,
            foreignWorkspace,
            "octagrid.bambu-mqtt",
            new Dictionary<string, string> { ["url"] = "192.168.1.9" },
            printerId: null,
            agentId: null,
            CancellationToken.None));
        Assert.Empty(_fixture.Link.Sent);
    }
}
