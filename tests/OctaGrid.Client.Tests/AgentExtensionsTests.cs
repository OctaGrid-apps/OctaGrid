using System.IO.Compression;
using System.Text;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.Tests;

/// <summary>Extensions belong to an agent: the dialog manages one agent at a time and sends every call to it.</summary>
public sealed class AgentExtensionsTests
{
    private static readonly Guid Workshop = Guid.NewGuid();
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid WorkshopAgent = Guid.NewGuid();
    private static readonly Guid OfficeAgent = Guid.NewGuid();

    private readonly FakeAgents _agents = new();
    private readonly RecordingExtensionsClient _extensions = new();
    private readonly InMemorySettingsStore _settings = new();

    private ExtensionsViewModel NewViewModel() =>
        new(_extensions, new AgentDirectory(_agents, _agents), settings: _settings);

    private void TwoAgents()
    {
        _agents.Add(Workshop, "Workshop", new AgentResponse(WorkshopAgent, "Workshop PC", "Active", null, 1));
        _agents.Add(Office, "Office", new AgentResponse(OfficeAgent, "Office Pi", "Active", null, 1));
        _extensions.Installed[WorkshopAgent] = [Extension("acme.workshop")];
        _extensions.Installed[OfficeAgent] = [Extension("acme.office")];
    }

    private static ExtensionInfo Extension(string id) =>
        new(id, id, "1.0.0", "Acme", "connector", ExtensionSource.UserFolder, ExtensionState.Loaded, null,
            ConnectorCapabilities.None, CanUninstall: true);

    [Fact]
    public async Task With_several_agents_the_dialog_offers_a_choice_and_starts_on_the_first()
    {
        TwoAgents();
        var viewModel = NewViewModel();

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSeveralAgents);
        Assert.Equal(["Workshop PC · Workshop", "Office Pi · Office"], viewModel.Agents.Select(a => a.DisplayName));
        Assert.Equal(WorkshopAgent, viewModel.SelectedAgent!.Id);
        Assert.Equal(["acme.workshop"], viewModel.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Picking_another_agent_shows_its_extensions()
    {
        TwoAgents();
        var viewModel = NewViewModel();
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SelectedAgent = viewModel.Agents.Single(a => a.Id == OfficeAgent);

        Assert.Equal(["acme.office"], viewModel.Items.Select(i => i.Id));
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Actions_go_to_the_selected_agent_and_developer_mode_remembers_it()
    {
        TwoAgents();
        var viewModel = NewViewModel();
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.SelectedAgent = viewModel.Agents.Single(a => a.Id == OfficeAgent);

        await viewModel.SetDeveloperFolderAsync("/home/pi/ext");

        Assert.Contains(("set-developer-folder", OfficeAgent), _extensions.Calls);
        Assert.DoesNotContain(_extensions.Calls, c => c.Call == "set-developer-folder" && c.AgentId != OfficeAgent);
        Assert.Equal(OfficeAgent, _settings.ClientSettings.DeveloperExtensionsAgentId);
    }

    [Fact]
    public async Task The_dialog_starts_on_the_agent_developer_mode_was_turned_on_for()
    {
        TwoAgents();
        _settings.Update(s => s with { DeveloperExtensionsFolder = "/home/pi/ext", DeveloperExtensionsAgentId = OfficeAgent });
        var viewModel = NewViewModel();

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(OfficeAgent, viewModel.SelectedAgent!.Id);
        Assert.Equal(["acme.office"], viewModel.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Without_an_agent_nothing_can_be_installed_or_changed()
    {
        var viewModel = NewViewModel();

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasNoAgent);
        Assert.False(viewModel.CanEdit);
        Assert.Empty(viewModel.Items);
        Assert.Empty(_extensions.Calls);
    }

    [Fact]
    public async Task Installing_sends_the_package_content_to_the_selected_agent()
    {
        TwoAgents();
        var viewModel = NewViewModel();
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.InstallAsync(_ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));

        Assert.Contains(("install", WorkshopAgent), _extensions.Calls);
        Assert.Equal([1, 2, 3], _extensions.LastPackage);
    }

    [Fact]
    public async Task A_package_that_cannot_be_read_is_reported_and_nothing_is_sent()
    {
        TwoAgents();
        var viewModel = NewViewModel();
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.InstallAsync(_ => throw new IOException("locked"));

        Assert.DoesNotContain(_extensions.Calls, c => c.Call == "install");
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public void At_startup_developer_mode_uses_the_saved_agent_or_else_the_only_one()
    {
        var a = new AgentChoice(Guid.NewGuid(), "A", "W");
        var b = new AgentChoice(Guid.NewGuid(), "B", "W");

        Assert.Equal(b, DeveloperMode.ChooseAgent([a, b], b.Id));
        Assert.Equal(a, DeveloperMode.ChooseAgent([a], savedAgentId: null));
        Assert.Equal(a, DeveloperMode.ChooseAgent([a], Guid.NewGuid()));
        Assert.Null(DeveloperMode.ChooseAgent([a, b], savedAgentId: null));
        Assert.Null(DeveloperMode.ChooseAgent([a, b], Guid.NewGuid()));
        Assert.Null(DeveloperMode.ChooseAgent([], savedAgentId: null));
    }

    [Fact]
    public async Task A_folder_is_zipped_with_every_file_under_its_path()
    {
        // Avalonia's storage items cannot be faked, so the zipping is tested on the files a folder walk yields.
        var files = new[]
        {
            new ExtensionFolderPackage.PackageFile("extension.json", () => Text("{}")),
            new ExtensionFolderPackage.PackageFile("lib/Acme.dll", () => Text("binary")),
        }.ToAsyncEnumerable();

        await using var zipped = await ExtensionFolderPackage.ZipAsync(files, TestContext.Current.CancellationToken);

        Assert.Equal(0, zipped.Position);
        using var archive = new ZipArchive(zipped, ZipArchiveMode.Read);
        Assert.Equal(["extension.json", "lib/Acme.dll"], archive.Entries.Select(e => e.FullName));
        using var reader = new StreamReader(archive.GetEntry("lib/Acme.dll")!.Open());
        Assert.Equal("binary", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));

        static Task<Stream> Text(string content) => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(content)));
    }

    private sealed class FakeAgents : IWorkspacesClient, IAgentsClient
    {
        private readonly List<(WorkspaceResponse Workspace, List<AgentResponse> Agents)> _workspaces = [];

        public void Add(Guid workspaceId, string name, AgentResponse agent) =>
            _workspaces.Add((new WorkspaceResponse(workspaceId, name, 1), [agent]));

        public Task<List<WorkspaceResponse>> GetWorkspaceList(CancellationToken cancellationToken) =>
            Task.FromResult(_workspaces.Select(w => w.Workspace).ToList());

        public Task<List<AgentResponse>> GetAgentList(Guid workspaceId, CancellationToken cancellationToken) =>
            Task.FromResult(_workspaces.Where(w => w.Workspace.Id == workspaceId).SelectMany(w => w.Agents).ToList());

        public Task<WorkspaceResponse> CreateWorkspace(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<List<WorkspaceSummaryResponse>> GetWorkspacesSummary(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WorkspaceResponse> UpdateWorkspace(Guid workspaceId, string name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AssignPrinter(Guid agentId, Guid printerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AgentResponse> CreateAgent(Guid workspaceId, string name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AgentResponse> UpdateAgent(Guid agentId, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingExtensionsClient : IExtensionsClient
    {
        private readonly Dictionary<Guid, string?> _developerFolders = [];

        public Dictionary<Guid, List<ExtensionInfo>> Installed { get; } = [];

        public List<(string Call, Guid AgentId)> Calls { get; } = [];

        public byte[]? LastPackage { get; private set; }

        public Task<IReadOnlyList<ExtensionInfo>> GetExtensionsAsync(Guid agentId, CancellationToken cancellationToken)
        {
            Calls.Add(("get", agentId));
            return Task.FromResult<IReadOnlyList<ExtensionInfo>>(Installed.GetValueOrDefault(agentId) ?? []);
        }

        public Task SetExtensionEnabledAsync(Guid agentId, string extensionId, bool enabled, CancellationToken cancellationToken)
        {
            Calls.Add(("set-enabled", agentId));
            return Task.CompletedTask;
        }

        public async Task<ExtensionInstallResult> InstallExtensionAsync(Guid agentId, Stream package, CancellationToken cancellationToken)
        {
            Calls.Add(("install", agentId));
            using var copy = new MemoryStream();
            await package.CopyToAsync(copy, cancellationToken);
            LastPackage = copy.ToArray();
            return ExtensionInstallResult.Ok("acme.new", "Installed.");
        }

        public Task<ExtensionInstallResult> ApproveExtensionAsync(Guid agentId, string extensionId, CancellationToken cancellationToken)
        {
            Calls.Add(("approve", agentId));
            return Task.FromResult(ExtensionInstallResult.Ok(extensionId, "Turned on."));
        }

        public Task<ExtensionInstallResult> UninstallExtensionAsync(Guid agentId, string extensionId, CancellationToken cancellationToken)
        {
            Calls.Add(("uninstall", agentId));
            return Task.FromResult(ExtensionInstallResult.Ok(extensionId, "Removed."));
        }

        public Task<string?> GetDeveloperFolderAsync(Guid agentId, CancellationToken cancellationToken)
        {
            Calls.Add(("get-developer-folder", agentId));
            return Task.FromResult(_developerFolders.GetValueOrDefault(agentId));
        }

        public Task<ExtensionInstallResult> SetDeveloperFolderAsync(Guid agentId, string? folder, CancellationToken cancellationToken)
        {
            Calls.Add(("set-developer-folder", agentId));
            _developerFolders[agentId] = folder;
            return Task.FromResult(ExtensionInstallResult.Ok(null, "Developer mode changed."));
        }

        public Task<ExtensionInstallResult> ReloadDeveloperExtensionsAsync(Guid agentId, CancellationToken cancellationToken)
        {
            Calls.Add(("reload", agentId));
            return Task.FromResult(ExtensionInstallResult.Ok(null, "Reloaded."));
        }
    }
}
