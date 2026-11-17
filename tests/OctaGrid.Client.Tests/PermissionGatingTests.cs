using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Controls;
using OctaGrid.Client.Converters;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Permissions;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.Tests;

/// <summary>What a user may not do is not shown: the buttons are gone, not disabled.</summary>
[Collection(UiCollection.Name)]
public class PermissionGatingTests
{
    private static readonly Permission[] Reader = [Permission.View];
    private static readonly Permission[] Operator = [Permission.View, Permission.ControlPrint, Permission.ManageFiles];

    private static WorkspacePermissions As(params Permission[] granted)
    {
        var state = new ApplicationState(new InMemorySettingsStore())
        {
            SelectedWorkspace = new Workspace { Id = Guid.NewGuid(), Name = "Lab", Permissions = granted },
        };
        return new WorkspacePermissions(state);
    }

    private static Button ButtonNamed(Control control, string name) =>
        control.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);

    private static PrinterFileRow ShowFileRow(WorkspacePermissions permissions)
    {
        var file = new PrintMetadataListItem(new PrintMetadata("benchy.gcode", 1024, "PLA", 0.4, 5400), []);
        var row = new PrinterFileRowViewModel(file, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, () => true, permissions);
        var control = new PrinterFileRow { DataContext = row };
        new Window { Content = control, Width = 900, Height = 200 }.Show();
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static PrinterDashboardCard ShowCard(WorkspacePermissions permissions)
    {
        var item = new PrinterStatusWidget { Id = Guid.NewGuid(), Name = "Voron", State = UniversalPrinterState.Printing, ConnectorId = "acme.printer" };
        var card = new PrinterDashboardCardViewModel(item, new DesignAgentCommands(), new ByteArrayImageCache(), permissions: permissions);
        var control = new PrinterDashboardCard { DataContext = card };
        new Window { Content = control, Width = 500, Height = 500 }.Show();
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    [Fact]
    public async Task A_reader_sees_no_print_or_file_buttons_on_a_file()
    {
        await HeadlessUi.Run(() =>
        {
            var row = ShowFileRow(As(Reader));

            Assert.False(ButtonNamed(row, "Print benchy.gcode").IsVisible);
            Assert.False(ButtonNamed(row, "More actions").IsVisible);
        });
    }

    [Fact]
    public async Task An_operator_sees_the_print_and_file_buttons_on_a_file()
    {
        await HeadlessUi.Run(() =>
        {
            var row = ShowFileRow(As(Operator));

            Assert.True(ButtonNamed(row, "Print benchy.gcode").IsVisible);
            Assert.True(ButtonNamed(row, "More actions").IsVisible);
        });
    }

    [Fact]
    public async Task A_reader_sees_no_stop_button_on_a_printer_card_and_an_operator_does()
    {
        await HeadlessUi.Run(() =>
        {
            Assert.False(ButtonNamed(ShowCard(As(Reader)), "Stop print").IsVisible);
            Assert.True(ButtonNamed(ShowCard(As(Operator)), "Stop print").IsVisible);
        });
    }

    [Fact]
    public void The_command_box_needs_the_permission_as_well_as_expert_mode()
    {
        var withoutPermission = new PrinterConsoleViewModel(
            Guid.NewGuid(), new DesignAgentCommands(), supportsCommands: true, permissions: As(Operator)) { IsExpertMode = true };
        var withPermission = new PrinterConsoleViewModel(
            Guid.NewGuid(), new DesignAgentCommands(), supportsCommands: true, permissions: As(Permission.View, Permission.SendCommands)) { IsExpertMode = true };

        Assert.False(withoutPermission.CanSend);
        Assert.True(withPermission.CanSend);
    }

    [Fact]
    public void Someone_who_may_not_send_commands_is_not_pointed_at_expert_mode()
    {
        var console = new PrinterConsoleViewModel(Guid.NewGuid(), new DesignAgentCommands(), supportsCommands: true, permissions: As(Reader));

        Assert.False(console.ShowExpertModeHint);
    }

    [Fact]
    public void An_extension_is_only_listed_for_someone_who_may_not_manage_extensions()
    {
        var info = new ExtensionInfo(
            "acme.printer", "Acme", "1.0.0", "Acme", "connector", ExtensionSource.UserFolder, ExtensionState.Loaded, null, ConnectorCapabilities.None, CanUninstall: true);

        var readOnly = new ExtensionItemViewModel(info, (_, _) => { }, _ => { }, _ => { }, canManage: false);
        var managed = new ExtensionItemViewModel(info, (_, _) => { }, _ => { }, _ => { });

        Assert.False(readOnly.CanToggle);
        Assert.False(readOnly.CanUninstall);
        Assert.False(readOnly.CanReview);
        Assert.True(managed.CanToggle);
        Assert.True(managed.CanUninstall);
    }

    [Fact]
    public async Task An_agent_is_offered_for_managing_only_where_the_workspace_allows_it()
    {
        var agents = new DesignOctaGridClient();

        var asReader = await new AgentDirectory(new OneWorkspace(Reader), agents).GetAgentsAsync(CancellationToken.None);
        var asManager = await new AgentDirectory(new OneWorkspace([Permission.View, Permission.ManageExtensions]), agents).GetAgentsAsync(CancellationToken.None);
        var notSaid = await new AgentDirectory(new OneWorkspace(null), agents).GetAgentsAsync(CancellationToken.None);

        Assert.All(asReader, agent => Assert.False(agent.CanManageExtensions));
        Assert.All(asManager, agent => Assert.True(agent.CanManageExtensions));
        Assert.All(notSaid, agent => Assert.True(agent.CanManageExtensions));
        Assert.NotEmpty(asReader);
    }

    [Fact]
    public void A_row_with_its_own_permissions_shows_only_what_they_allow()
    {
        var converter = HasPermissionConverter.Instance;

        Assert.Equal(false, converter.Convert(Reader, typeof(bool), Permission.ManageWorkspace, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(true, converter.Convert(Reader, typeof(bool), Permission.View, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(true, converter.Convert(null, typeof(bool), Permission.ManageWorkspace, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Views_are_told_when_another_workspace_changes_what_is_allowed()
    {
        var state = new ApplicationState(new InMemorySettingsStore())
        {
            SelectedWorkspace = new Workspace { Id = Guid.NewGuid(), Name = "As a reader", Permissions = Reader },
        };
        var permissions = new WorkspacePermissions(state);
        var notified = 0;
        permissions.PropertyChanged += (_, _) => notified++;

        Assert.False(permissions.CanControlPrint);
        state.SelectedWorkspace = new Workspace { Id = Guid.NewGuid(), Name = "As an operator", Permissions = Operator };

        Assert.True(permissions.CanControlPrint);
        Assert.True(permissions.CanManageFiles);
        Assert.False(permissions.CanManagePrinters);
        Assert.True(notified > 0);
    }

    private sealed class OneWorkspace : IWorkspacesClient
    {
        private readonly IReadOnlyList<Permission>? _permissions;

        public OneWorkspace(IReadOnlyList<Permission>? permissions)
        {
            _permissions = permissions;
        }

        public Task<WorkspaceResponse> CreateWorkspace(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<List<WorkspaceResponse>> GetWorkspaceList(CancellationToken cancellationToken) =>
            Task.FromResult<List<WorkspaceResponse>>([new WorkspaceResponse(DesignOctaGridClient.DefaultWorkspaceId, "Lab", 1, _permissions)]);

        public Task<List<WorkspaceSummaryResponse>> GetWorkspacesSummary(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WorkspaceResponse> UpdateWorkspace(Guid workspaceId, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
