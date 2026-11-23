using OctaGrid.Application.Agents;
using OctaGrid.Application.Authorization;
using OctaGrid.Application.Services;
using OctaGrid.Application.Telemetry;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Dashboards;
using OctaGrid.Protocol.Permissions;
using OctaGrid.Protocol.Printers;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Application.Tests;

/// <summary>
/// Every action a user can ask for, with the permission it needs. Each is tried as every role: it is forbidden
/// exactly when the role lacks the permission, and then nothing reaches an agent. A new service method belongs
/// in <see cref="Actions"/>.
/// </summary>
public sealed class RoleEnforcementTests : IDisposable
{
    private const string Connector = "octagrid.bambu-mqtt";

    private static readonly IReadOnlyDictionary<string, string> Url = new Dictionary<string, string> { ["url"] = "192.168.1.9" };
    private static readonly IReadOnlyDictionary<string, string> NoSecrets = new Dictionary<string, string>();

    private static readonly Dictionary<string, (Permission Needs, Func<Scene, Guid, Task> Run)> Actions = new()
    {
        ["printers.get"] = (Permission.View, (s, u) => s.Printers.GetPrinter(u, s.WorkspaceId, s.PrinterId, None)),
        ["printers.list"] = (Permission.View, (s, u) => s.Printers.GetPrintersList(u, s.WorkspaceId, new GetPrintersQuery(null), None)),
        ["printers.create"] = (Permission.ManagePrinters, (s, u) => s.Printers.Create(u, s.WorkspaceId, new CreatePrinterRequest("New", Connector, Url, NoSecrets), None)),
        ["printers.update"] = (Permission.ManagePrinters, (s, u) => s.Printers.Update(u, s.PrinterId, new UpdatePrinterRequest("Renamed", Connector, Url, null), None)),
        ["printers.rename"] = (Permission.ManagePrinters, (s, u) => s.Printers.UpdateIdentity(u, s.PrinterId, new UpdatePrinterIdentityRequest("Renamed"), None)),
        ["printers.enable"] = (Permission.ManagePrinters, (s, u) => s.Printers.SetEnabled(u, s.PrinterId, false, None)),
        ["printers.delete"] = (Permission.ManagePrinters, (s, u) => s.Printers.Delete(u, s.PrinterId, None)),

        ["print.start"] = (Permission.ControlPrint, (s, u) => s.Commands.StartPrint(u, s.PrinterId, "a.gcode", None)),
        ["print.pause"] = (Permission.ControlPrint, (s, u) => s.Commands.PausePrint(u, s.PrinterId, None)),
        ["print.resume"] = (Permission.ControlPrint, (s, u) => s.Commands.ResumePrint(u, s.PrinterId, None)),
        ["print.stop"] = (Permission.ControlPrint, (s, u) => s.Commands.StopPrint(u, s.PrinterId, None)),
        ["files.list"] = (Permission.View, (s, u) => s.Commands.GetFiles(u, s.PrinterId, None)),
        ["files.upload"] = (Permission.ManageFiles, (s, u) => s.Commands.UploadFile(u, s.PrinterId, new MemoryStream([1, 2, 3]), "a.gcode", None)),
        ["files.delete"] = (Permission.ManageFiles, (s, u) => s.Commands.DeleteFile(u, s.PrinterId, "a.gcode", None)),
        ["console.send"] = (Permission.SendCommands, (s, u) => s.Commands.SendConsoleCommand(u, s.PrinterId, "M105", None)),
        ["telemetry.watch"] = (Permission.View, (s, u) => s.Watch.Watch(u, Guid.NewGuid(), [s.PrinterId], null, None)),
        ["connection.test-saved"] = (Permission.ManagePrinters, (s, u) => s.Commands.TestConnection(u, s.WorkspaceId, Connector, Url, s.PrinterId, null, None)),
        ["connection.test-new"] = (Permission.ManagePrinters, (s, u) => s.Commands.TestConnection(u, s.WorkspaceId, Connector, Url, null, s.AgentId, None)),
        ["connection.test-new-default-agent"] = (Permission.ManagePrinters, (s, u) => s.Commands.TestConnection(u, s.WorkspaceId, Connector, Url, null, null, None)),
        ["connectors.list"] = (Permission.View, (s, u) => s.Commands.GetConnectors(u, s.AgentId, None)),

        ["dashboards.list"] = (Permission.View, (s, u) => s.Dashboards.GetList(u, s.WorkspaceId, None)),
        ["dashboards.create"] = (Permission.ManageDashboards, (s, u) => s.Dashboards.Create(u, s.WorkspaceId, new CreateDashboardRequest("New", DashboardType.List), None)),
        ["dashboards.update"] = (Permission.ManageDashboards, (s, u) => s.Dashboards.Update(u, s.DashboardId, new UpdateDashboardRequest("Renamed"), None)),
        ["dashboards.set-default"] = (Permission.ManageDashboards, (s, u) => s.Dashboards.SetDefault(u, s.DashboardId, None)),
        ["dashboards.delete"] = (Permission.ManageDashboards, (s, u) => s.Dashboards.Delete(u, s.DashboardId, None)),

        ["agents.list"] = (Permission.View, (s, u) => s.Agents.GetList(u, s.WorkspaceId, None)),
        ["agents.create"] = (Permission.ManageAgents, (s, u) => s.Agents.Create(u, s.WorkspaceId, new CreateAgentRequest("New"), None)),
        ["agents.update"] = (Permission.ManageAgents, (s, u) => s.Agents.Update(u, s.AgentId, new UpdateAgentRequest("Renamed"), None)),
        ["agents.assign-printer"] = (Permission.ManagePrinters, (s, u) => s.Agents.AssignPrinter(u, s.AgentId, s.PrinterId, None)),

        ["extensions.list"] = (Permission.View, (s, u) => s.Extensions.GetList(u, s.AgentId, None)),
        ["extensions.dev-folder.get"] = (Permission.View, (s, u) => s.Extensions.GetDeveloperFolder(u, s.AgentId, None)),
        ["extensions.set-enabled"] = (Permission.ManageExtensions, (s, u) => s.Extensions.SetEnabled(u, s.AgentId, "acme.printer", false, None)),
        ["extensions.install"] = (Permission.ManageExtensions, (s, u) => s.Extensions.Install(u, s.AgentId, new MemoryStream([1, 2, 3]), None)),
        ["extensions.approve"] = (Permission.ManageExtensions, (s, u) => s.Extensions.Approve(u, s.AgentId, "acme.printer", None)),
        ["extensions.uninstall"] = (Permission.ManageExtensions, (s, u) => s.Extensions.Uninstall(u, s.AgentId, "acme.printer", None)),
        ["extensions.dev-folder.set"] = (Permission.ManageExtensions, (s, u) => s.Extensions.SetDeveloperFolder(u, s.AgentId, "C:/dev", None)),
        ["extensions.dev-folder.reload"] = (Permission.ManageExtensions, (s, u) => s.Extensions.ReloadDeveloperExtensions(u, s.AgentId, None)),

        ["workspace.update"] = (Permission.ManageWorkspace, (s, u) => s.Workspaces.Update(u, s.WorkspaceId, new UpdateWorkspaceRequest("Renamed"), None)),
    };

    private readonly AgentLinkFixture _fixture = new();
    private readonly RolePermissionPolicy _policy = new();

    private static CancellationToken None => CancellationToken.None;

    public static TheoryData<Role, string> Cases()
    {
        var cases = new TheoryData<Role, string>();
        foreach (var role in Enum.GetValues<Role>())
        {
            foreach (var action in Actions.Keys)
            {
                cases.Add(role, action);
            }
        }

        return cases;
    }

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task An_action_is_forbidden_exactly_when_the_role_lacks_its_permission(Role role, string action)
    {
        var (needs, run) = Actions[action];
        var printer = await _fixture.CreatePrinter();
        Guid dashboardId;
        await using (var setup = _fixture.NewContext())
        {
            // Two, so the second is neither the default nor the last one and can be changed or deleted.
            var dashboards = new DashboardsService(setup, TestAccess.For(setup));
            await dashboards.Create(_fixture.UserId, _fixture.WorkspaceId, new CreateDashboardRequest("First", DashboardType.List), None);
            dashboardId = (await dashboards.Create(_fixture.UserId, _fixture.WorkspaceId, new CreateDashboardRequest("Second", DashboardType.List), None)).Id;
        }

        var userId = _fixture.AddMember(role);

        await using var context = _fixture.NewContext();
        var thrown = await Record.ExceptionAsync(() => run(new Scene(_fixture, context, printer.Id, dashboardId), userId));

        if (_policy.For(role).Contains(needs))
        {
            Assert.Null(thrown);
        }
        else
        {
            Assert.IsType<ForbiddenException>(thrown);
            Assert.Empty(_fixture.Link.Sent);
        }
    }

    /// <summary>The services over one context, and the things in the workspace an action can be tried on.</summary>
    private sealed class Scene
    {
        public Scene(AgentLinkFixture fixture, OctaGridContext context, Guid printerId, Guid dashboardId)
        {
            var access = TestAccess.For(context);
            WorkspaceId = fixture.WorkspaceId;
            AgentId = fixture.AgentId;
            PrinterId = printerId;
            DashboardId = dashboardId;
            Printers = fixture.Printers(context);
            var routing = new PrinterRouting(context, access);
            Commands = new PrinterCommandsService(access, routing, fixture.Secrets(context), fixture.Link);
            Watch = new PrinterWatchService(routing, fixture.Link, new PrinterWatchRegistry());
            Extensions = new ExtensionsService(access, fixture.Link);
            Dashboards = new DashboardsService(context, access);
            Agents = new AgentsService(context, access, fixture.Link);
            Workspaces = new WorkspacesService(context, access);
        }

        public Guid AgentId { get; }

        public AgentsService Agents { get; }

        public PrinterCommandsService Commands { get; }

        public Guid DashboardId { get; }

        public DashboardsService Dashboards { get; }

        public ExtensionsService Extensions { get; }

        public Guid PrinterId { get; }

        public PrintersService Printers { get; }

        public PrinterWatchService Watch { get; }

        public Guid WorkspaceId { get; }

        public WorkspacesService Workspaces { get; }
    }
}
