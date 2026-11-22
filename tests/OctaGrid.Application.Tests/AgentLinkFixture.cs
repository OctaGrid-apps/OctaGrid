using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OctaGrid.Application.Agents;
using OctaGrid.Application.Secrets;
using OctaGrid.Application.Services;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.AgentLink.Replies;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Tests;

/// <summary>
/// A real (in-memory) SQLite database with one user in one workspace that has one agent, and a link that records
/// what is sent instead of reaching an agent. For the services that talk to agents.
/// </summary>
internal sealed class AgentLinkFixture : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AesGcmSecretProtector _protector = new(new FixedKeySource());

    public AgentLinkFixture()
    {
        _connection.Open();
        using var context = NewContext();
        context.Database.EnsureCreated();

        context.Users.Add(new User { Id = UserId, Name = "Test", Email = "test@example.com", PasswordHash = "x" });
        context.Workspaces.Add(TestData.Workspace(WorkspaceId, "Test workspace", UserId));
        context.Agents.Add(new Agent { Id = AgentId, Name = "Agent", WorkspaceId = WorkspaceId });
        context.SaveChanges();
    }

    public Guid AgentId { get; } = Guid.NewGuid();

    public RecordingAgentLink Link { get; } = new();

    public Guid UserId { get; } = Guid.NewGuid();

    public Guid WorkspaceId { get; } = Guid.NewGuid();

    /// <summary>Another member of the workspace, with the given role.</summary>
    public Guid AddMember(Role role)
    {
        using var context = NewContext();
        var user = new User { Id = Guid.NewGuid(), Name = role.ToString(), Email = $"{Guid.NewGuid():N}@example.com", PasswordHash = "x" };
        context.Users.Add(user);
        context.SaveChanges();
        TestData.AddMember(context, WorkspaceId, user.Id, role);
        return user.Id;
    }

    /// <summary>An agent in a workspace the test user is not a member of.</summary>
    public Guid AddForeignAgent()
    {
        using var context = NewContext();
        var owner = new User { Id = Guid.NewGuid(), Name = "Someone else", Email = $"{Guid.NewGuid():N}@example.com", PasswordHash = "x" };
        var workspace = TestData.Workspace(Guid.NewGuid(), "Someone else's", owner.Id);
        var agent = new Agent { Id = Guid.NewGuid(), Name = "Foreign", WorkspaceId = workspace.Id };
        context.Users.Add(owner);
        context.Workspaces.Add(workspace);
        context.Agents.Add(agent);
        context.SaveChanges();
        return agent.Id;
    }

    public async Task<PrinterResponse> CreatePrinter(bool enabled = true, Guid? agentId = null)
    {
        await using var context = NewContext();
        return await Printers(context).Create(
            UserId,
            WorkspaceId,
            new CreatePrinterRequest(
                "Printer",
                "octagrid.bambu-mqtt",
                new Dictionary<string, string> { ["url"] = "192.168.1.5" },
                new Dictionary<string, string> { ["apiKey"] = "123456" },
                enabled,
                agentId),
            CancellationToken.None);
    }

    public void Dispose() => _connection.Dispose();

    public OctaGridContext NewContext(IOutboxSignal? outboxSignal = null) =>
        new(new DbContextOptionsBuilder<OctaGridContext>().UseSqlite(_connection).Options, outboxSignal);

    public PrintersService Printers(OctaGridContext context) => new(context, TestAccess.For(context), _protector);

    public static PrinterRouting Routing(OctaGridContext context) => new(context, TestAccess.For(context));

    /// <summary>Takes the printer away from its agent, as when the agent is removed.</summary>
    public void UnassignPrinter(Guid printerId)
    {
        using var context = NewContext();
        context.Printers.Single(p => p.Id == printerId).Unassign();
        context.SaveChanges();
    }

    private sealed class FixedKeySource : ISecretKeySource
    {
        public byte[] GetOrCreateKey() => Enumerable.Range(1, AesGcmSecretProtector.KeySize).Select(i => (byte)i).ToArray();
    }
}

/// <summary>Records every request with the agent it was sent to, and answers with an empty success.</summary>
internal sealed class RecordingAgentLink : IAgentLink
{
    public List<(Guid AgentId, AgentRequest Request)> Sent { get; } = [];

    public Guid UploadId { get; } = Guid.NewGuid();

    /// <summary>Agents that are not connected: requests to them fail as the real link fails them.</summary>
    public HashSet<Guid> Unreachable { get; } = [];

    /// <summary>Requests of this type are refused by the agent with a validation error, as an agent that rejects them.</summary>
    public Type? Refused { get; set; }

    public Task<TReply> SendAsync<TReply>(Guid agentId, AgentRequest<TReply> request, CancellationToken cancellationToken)
        where TReply : AgentReply
    {
        if (Unreachable.Contains(agentId))
        {
            throw AgentHandshake.Unavailable(agentId, AgentConnectionState.Offline);
        }

        if (request.GetType() == Refused)
        {
            throw new ValidationException("request", "The agent refused the request.");
        }

        Sent.Add((agentId, request));

        AgentReply reply = request switch
        {
            // Small pieces, so a short stream still arrives as several of them.
            BeginUploadRequest => new UploadStartedReply(UploadId, MaxChunkSize: 4),
            ListPrinterFilesRequest => new PrinterFilesReply([]),
            GetConnectorsRequest => new ConnectorsReply(Connectors.ToList()),
            GetExtensionsRequest => new ExtensionsReply([]),
            GetDeveloperFolderRequest => new DeveloperFolderReply(null),
            // Each printer as running, so a test can tell an answer from the agent apart from the offline fallback.
            GetPrinterBackfillRequest r => new PrinterBackfillReply(r.Printers
                .Select(p => new PrinterBackfill(
                    p.PrinterId,
                    new PrinterTelemetry(p.PrinterId) { State = UniversalPrinterState.Idle },
                    []))
                .ToList()),
            TestConnectionRequest => new ConnectionTestReply(Extensions.Models.ConnectionTestResult.Ok()),
            AgentRequest<ExtensionChangeReply> => new ExtensionChangeReply(ExtensionInstallResult.Ok(null, "Done")),
            AgentRequest<AgentAck> => new AgentAck(),
            _ => throw new NotSupportedException($"The recording link has no answer for {request.GetType().Name}."),
        };

        return Task.FromResult((TReply)reply);
    }

    private readonly AgentNotificationFanOut _fanOut = new();

    /// <summary>The connectors every agent answers <see cref="GetConnectorsRequest"/> with.</summary>
    public List<ConnectorDescriptor> Connectors { get; } = [];

    public AgentConnectionState GetConnectionState(Guid agentId) =>
        Unreachable.Contains(agentId) ? AgentConnectionState.Offline : AgentConnectionState.Online;

    public AgentNotificationSubscription Subscribe(AgentNotificationChannel channel) => _fanOut.Subscribe(channel);

    /// <summary>Reports a notification from an agent to whoever listens, as the real link does.</summary>
    public void Notify(Guid agentId, AgentNotification notification)
    {
        _fanOut.Publish(new AgentNotificationEnvelope(agentId, notification));
    }
}
