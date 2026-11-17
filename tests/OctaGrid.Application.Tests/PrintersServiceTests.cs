using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OctaGrid.Application.Secrets;
using OctaGrid.Application.Services;
using OctaGrid.Application.Telemetry;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Permissions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Tests;

/// <summary>Runs the printers service against a real (in-memory) SQLite database.</summary>
public sealed class PrintersServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _workspaceId = Guid.NewGuid();
    // Version 7, like the ids the context gives new rows, and older than any of them: ids sort by creation.
    private readonly Guid _agentId = Guid.CreateVersion7(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly RecordingPublisher _publisher = new();
    private readonly FixedKeySource _keySource = new();
    private readonly AesGcmSecretProtector _protector;

    public PrintersServiceTests()
    {
        _protector = new AesGcmSecretProtector(_keySource);
        Initialize();
    }

    private void Initialize()
    {
        _connection.Open();
        using var context = NewContext();
        context.Database.EnsureCreated();

        context.Users.Add(new User { Id = _userId, Name = "Test", Email = "test@example.com", PasswordHash = "x" });
        context.Workspaces.Add(new Workspace { Id = _workspaceId, Name = "Test workspace" });
        context.Agents.Add(new Agent { Id = _agentId, Name = "Agent", WorkspaceId = _workspaceId });
        context.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceId,
            UserId = _userId,
            Role = Role.Owner,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class RecordingPublisher : IMessagePublisher
    {
        public List<EntityChanged> Published { get; } = [];

        public void Publish(EntityChanged entity) => Published.Add(entity);
    }

    private OctaGridContext NewContext() =>
        new(new DbContextOptionsBuilder<OctaGridContext>().UseSqlite(_connection).Options);

    private PrintersService NewService(OctaGridContext context) => new(context, TestAccess.For(context), _publisher, _protector);

    /// <summary>What is saved for a printer, read from its row and decrypted the way the service does it.</summary>
    private IReadOnlyDictionary<string, string> SavedSecrets(Guid printerId)
    {
        using var context = NewContext();
        var saved = context.Printers.AsNoTracking().SingleOrDefault(p => p.Id == printerId)?.ProtectedSecrets;
        return saved is null
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(_protector.Unprotect(saved, printerId.ToString("N")))!;
    }

    private sealed class FixedKeySource : ISecretKeySource
    {
        public byte[] Key { get; set; } = Enumerable.Range(1, AesGcmSecretProtector.KeySize).Select(i => (byte)i).ToArray();

        public byte[] GetOrCreateKey() => Key;
    }

    private static Dictionary<string, string> Settings(string url = "192.168.1.5", string? serial = null)
    {
        var settings = new Dictionary<string, string> { ["url"] = url };
        if (serial is not null)
        {
            settings["serialNumber"] = serial;
        }

        return settings;
    }

    private async Task<PrinterResponse> CreatePrinter(
        Dictionary<string, string>? settings = null,
        Dictionary<string, string>? secrets = null,
        bool enabled = true,
        string connectorId = "octagrid.bambu-mqtt")
    {
        await using var context = NewContext();
        return await NewService(context).Create(
            _userId,
            _workspaceId,
            new CreatePrinterRequest(
                "Printer", connectorId, settings ?? Settings(), secrets ?? new Dictionary<string, string> { ["apiKey"] = "123456" }, enabled),
            CancellationToken.None);
    }

    [Fact]
    public async Task Create_stores_the_connector_settings_and_secrets()
    {
        var created = await CreatePrinter(Settings(serial: "01P"), new Dictionary<string, string> { ["apiKey"] = "secret" });

        Assert.Equal("octagrid.bambu-mqtt", created.ConnectorId);
        Assert.Equal("192.168.1.5", created.Settings["url"]);
        Assert.Equal("01P", created.Settings["serialNumber"]);
        Assert.Equal(["apiKey"], created.SecretKeys);
        Assert.Equal("secret", SavedSecrets(created.Id)["apiKey"]);
        Assert.DoesNotContain("apiKey", created.Settings.Keys);
        Assert.Contains(_publisher.Published, p => p.EntityId == created.Id && p.Kind == ChangeKind.Upserted);
    }

    [Fact]
    public async Task Secret_values_are_kept_out_of_the_database()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "s3cret-value" });

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM Printers";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        var row = string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i).ToString()));

        Assert.Contains(created.Id.ToString(), row, StringComparison.OrdinalIgnoreCase);
        // The names of the secrets are encrypted together with the values.
        Assert.DoesNotContain("apiKey", row);
        Assert.DoesNotContain("s3cret-value", row);
    }

    [Fact]
    public async Task Settings_survive_a_round_trip_and_a_reader_gets_only_the_secret_keys()
    {
        var created = await CreatePrinter(Settings("printer.local:7125"), new Dictionary<string, string> { ["token"] = "t\"o'k\\en" });

        await using var context = NewContext();
        var service = NewService(context);
        var loaded = await service.GetPrinter(_userId, _workspaceId, created.Id, CancellationToken.None);
        var listed = Assert.Single(await service.GetPrintersList(_userId, _workspaceId, new GetPrintersQuery(null), CancellationToken.None));

        Assert.Equal("printer.local:7125", loaded.Settings["url"]);
        Assert.Equal(["token"], loaded.SecretKeys);
        Assert.Equal(["token"], listed.SecretKeys);
        Assert.Equal("t\"o'k\\en", SavedSecrets(created.Id)["token"]);
    }

    [Fact]
    public async Task A_printer_without_secrets_has_no_secret_keys()
    {
        var created = await CreatePrinter(secrets: []);

        await using var context = NewContext();
        var loaded = await NewService(context).GetPrinter(_userId, _workspaceId, created.Id, CancellationToken.None);

        Assert.Empty(loaded.SecretKeys);
        Assert.Empty(SavedSecrets(created.Id));
    }

    [Fact]
    public async Task An_empty_secret_is_not_saved()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "" });

        Assert.Empty(created.SecretKeys);
        Assert.Empty(SavedSecrets(created.Id));
    }

    [Fact]
    public async Task Update_keeps_a_saved_secret_that_is_left_out()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "old" });

        await using var context = NewContext();
        var updated = await NewService(context).Update(
            _userId,
            created.Id,
            new UpdatePrinterRequest("Renamed", "octagrid.bambu-mqtt", Settings("10.0.0.9"), Secrets: null),
            CancellationToken.None);

        Assert.Equal(["apiKey"], updated.SecretKeys);
        Assert.Equal("old", SavedSecrets(created.Id)["apiKey"]);
    }

    [Fact]
    public async Task Update_replaces_a_secret_that_is_sent_and_removes_one_sent_empty()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "old", ["token"] = "t" });

        await using var context = NewContext();
        var updated = await NewService(context).Update(
            _userId,
            created.Id,
            new UpdatePrinterRequest(
                "Printer", "octagrid.bambu-mqtt", Settings(), new Dictionary<string, string> { ["apiKey"] = "new", ["token"] = "" }),
            CancellationToken.None);

        Assert.Equal(["apiKey"], updated.SecretKeys);
        Assert.Equal("new", Assert.Single(SavedSecrets(created.Id)).Value);
    }

    [Fact]
    public async Task Update_without_secret_changes_leaves_the_encrypted_text_as_it_is()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "old" });
        string? Stored()
        {
            using var read = NewContext();
            return read.Printers.AsNoTracking().Single(p => p.Id == created.Id).ProtectedSecrets;
        }

        var before = Stored();
        await using var context = NewContext();
        await NewService(context).Update(
            _userId, created.Id, new UpdatePrinterRequest("Renamed", "octagrid.bambu-mqtt", Settings(), Secrets: null), CancellationToken.None);

        Assert.NotNull(before);
        Assert.Equal(before, Stored());
    }

    [Fact]
    public async Task Secrets_that_cannot_be_decrypted_count_as_missing_and_can_be_entered_again()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "old" });

        // The database was moved to a machine with another key.
        _keySource.Key = Enumerable.Repeat((byte)7, AesGcmSecretProtector.KeySize).ToArray();

        await using var context = NewContext();
        var service = NewService(context);
        var profile = Assert.Single(await service.GetPrinterProfiles(_agentId, CancellationToken.None));
        Assert.Null(profile.Get("apiKey"));
        // The form must not claim a secret is saved when it can no longer be read.
        Assert.Empty((await service.GetPrinter(_userId, _workspaceId, created.Id, CancellationToken.None)).SecretKeys);

        var updated = await service.Update(
            _userId,
            created.Id,
            new UpdatePrinterRequest("Printer", "octagrid.bambu-mqtt", Settings(), new Dictionary<string, string> { ["apiKey"] = "again" }),
            CancellationToken.None);

        Assert.Equal(["apiKey"], updated.SecretKeys);
        Assert.Equal("again", SavedSecrets(created.Id)["apiKey"]);
    }

    [Fact]
    public async Task Encrypted_secrets_copied_to_another_printer_cannot_be_read_there()
    {
        var source = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "source-secret" });
        var target = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "target-secret" });

        await using (var context = NewContext())
        {
            var stolen = context.Printers.AsNoTracking().Single(p => p.Id == source.Id).ProtectedSecrets;
            context.Printers.Single(p => p.Id == target.Id).ProtectedSecrets = stolen;
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext();
        var profiles = await NewService(verify).GetPrinterProfiles(_agentId, CancellationToken.None);

        Assert.Null(profiles.Single(p => p.PrinterId == target.Id).Get("apiKey"));
        Assert.Equal("source-secret", profiles.Single(p => p.PrinterId == source.Id).Get("apiKey"));
    }

    [Fact]
    public async Task A_connection_test_does_not_fill_in_a_secret_that_is_being_removed()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "saved" });

        await using var context = NewContext();
        var settings = await NewService(context).AddSavedSecrets(
            _userId,
            created.Id,
            new Dictionary<string, string> { ["url"] = "10.0.0.9", ["apiKey"] = "" },
            CancellationToken.None);

        Assert.Equal(["url"], settings.Keys);
    }

    [Fact]
    public async Task Saved_secrets_fill_the_ones_left_out_when_testing_a_connection()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "saved", ["token"] = "saved-token" });

        await using var context = NewContext();
        var settings = await NewService(context).AddSavedSecrets(
            _userId,
            created.Id,
            new Dictionary<string, string> { ["url"] = "10.0.0.9", ["token"] = "typed" },
            CancellationToken.None);

        Assert.Equal("10.0.0.9", settings["url"]);
        Assert.Equal("saved", settings["apiKey"]);
        Assert.Equal("typed", settings["token"]);
    }

    [Fact]
    public async Task Saved_secrets_are_not_given_to_a_user_outside_the_workspace()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "saved" });

        await using var context = NewContext();
        await Assert.ThrowsAsync<AuthenticationException>(() => NewService(context).AddSavedSecrets(
            Guid.NewGuid(), created.Id, new Dictionary<string, string>(), CancellationToken.None));
    }

    [Fact]
    public async Task Changing_the_connector_replaces_the_settings_and_drops_the_saved_secrets()
    {
        var created = await CreatePrinter(Settings(serial: "01P"), new Dictionary<string, string> { ["apiKey"] = "old" });

        await using (var context = NewContext())
        {
            await NewService(context).Update(
                _userId,
                created.Id,
                new UpdatePrinterRequest("Renamed", "octagrid.moonraker", Settings("10.0.0.9"), new Dictionary<string, string>()),
                CancellationToken.None);
        }

        await using var verify = NewContext();
        var loaded = await NewService(verify).GetPrinter(_userId, _workspaceId, created.Id, CancellationToken.None);
        Assert.Equal("Renamed", loaded.Name);
        Assert.Equal("octagrid.moonraker", loaded.ConnectorId);
        Assert.Equal("10.0.0.9", loaded.Settings["url"]);
        Assert.DoesNotContain("serialNumber", loaded.Settings.Keys);
        Assert.Empty(loaded.SecretKeys);
        Assert.Empty(SavedSecrets(created.Id));
    }

    [Fact]
    public async Task Printer_profiles_merge_settings_and_secrets_for_enabled_printers_only()
    {
        var on = await CreatePrinter(Settings("192.168.1.5", "01P"), new Dictionary<string, string> { ["apiKey"] = "123456" });
        await CreatePrinter(enabled: false);

        await using var context = NewContext();
        var profiles = await NewService(context).GetPrinterProfiles(_agentId, CancellationToken.None);

        var profile = Assert.Single(profiles);
        Assert.Equal(on.Id, profile.PrinterId);
        Assert.Equal("octagrid.bambu-mqtt", profile.ConnectorId);
        Assert.Equal("192.168.1.5", profile.Get("url"));
        Assert.Equal("01P", profile.Get("serialNumber"));
        Assert.Equal("123456", profile.Get("apiKey"));
    }

    [Fact]
    public async Task A_secret_wins_over_a_setting_with_the_same_key()
    {
        await CreatePrinter(
            new Dictionary<string, string> { ["url"] = "x", ["apiKey"] = "plain" },
            new Dictionary<string, string> { ["apiKey"] = "secret" });

        await using var context = NewContext();
        var profile = Assert.Single(await NewService(context).GetPrinterProfiles(_agentId, CancellationToken.None));

        Assert.Equal("secret", profile.Get("apiKey"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Not A Connector")]
    [InlineData("moonraker")]
    public async Task Create_rejects_an_invalid_connector_id(string connectorId)
    {
        await using var context = NewContext();

        await Assert.ThrowsAsync<ValidationException>(() => NewService(context).Create(
            _userId,
            _workspaceId,
            new CreatePrinterRequest("Printer", connectorId, Settings(), new Dictionary<string, string>()),
            CancellationToken.None));
    }

    [Fact]
    public async Task Create_rejects_a_missing_name()
    {
        await using var context = NewContext();

        await Assert.ThrowsAsync<ValidationException>(() => NewService(context).Create(
            _userId,
            _workspaceId,
            new CreatePrinterRequest("", "octagrid.moonraker", Settings(), new Dictionary<string, string>()),
            CancellationToken.None));
    }

    [Fact]
    public async Task Update_requires_settings()
    {
        var created = await CreatePrinter();
        await using var context = NewContext();

        await Assert.ThrowsAsync<ValidationException>(() => NewService(context).Update(
            _userId,
            created.Id,
            new UpdatePrinterRequest("Printer", "octagrid.moonraker", Settings: null, Secrets: null),
            CancellationToken.None));
    }

    [Fact]
    public async Task A_user_outside_the_workspace_cannot_create_printers()
    {
        await using var context = NewContext();

        await Assert.ThrowsAsync<AuthenticationException>(() => NewService(context).Create(
            Guid.NewGuid(),
            _workspaceId,
            new CreatePrinterRequest("Printer", "octagrid.moonraker", Settings(), new Dictionary<string, string>()),
            CancellationToken.None));
    }

    private Guid AddAgent(Guid? workspaceId = null)
    {
        using var context = NewContext();
        var agent = new Agent { Name = "Another agent", WorkspaceId = workspaceId ?? _workspaceId };
        context.Agents.Add(agent);
        context.SaveChanges();
        return agent.Id;
    }

    private Guid AddWorkspace()
    {
        using var context = NewContext();
        // The user belongs to it too, so what is checked is the workspace match, not access.
        var workspace = new Workspace { Name = "Other workspace" };
        workspace.Members.Add(new WorkspaceMember { UserId = _userId, Role = Role.Owner, JoinedAt = DateTimeOffset.UtcNow });
        context.Workspaces.Add(workspace);
        context.SaveChanges();
        return workspace.Id;
    }

    [Fact]
    public async Task A_new_printer_gets_the_only_agent_of_its_workspace()
    {
        var created = await CreatePrinter();

        Assert.Equal(_agentId, created.AgentId);
        await using var context = NewContext();
        Assert.Equal(_agentId, await NewService(context).GetAgentId(_userId, created.Id, Permission.View, CancellationToken.None));
    }

    [Fact]
    public async Task A_new_printer_gets_no_agent_when_the_workspace_has_several_and_none_is_asked_for()
    {
        AddAgent();

        var created = await CreatePrinter();

        Assert.Null(created.AgentId);
        await using var context = NewContext();
        await Assert.ThrowsAsync<ValidationException>(() => NewService(context).GetAgentId(_userId, created.Id, Permission.View, CancellationToken.None));
    }

    [Fact]
    public async Task A_new_printer_gets_the_agent_asked_for()
    {
        var other = AddAgent();

        await using var context = NewContext();
        var created = await NewService(context).Create(
            _userId,
            _workspaceId,
            new CreatePrinterRequest("Printer", "octagrid.moonraker", Settings(), new Dictionary<string, string>(), AgentId: other),
            CancellationToken.None);

        Assert.Equal(other, created.AgentId);
    }

    [Fact]
    public async Task A_new_printer_cannot_get_an_agent_of_another_workspace()
    {
        var foreign = AddAgent(AddWorkspace());

        await using var context = NewContext();
        await Assert.ThrowsAsync<ValidationException>(() => NewService(context).Create(
            _userId,
            _workspaceId,
            new CreatePrinterRequest("Printer", "octagrid.moonraker", Settings(), new Dictionary<string, string>(), AgentId: foreign),
            CancellationToken.None));
    }

    [Fact]
    public async Task An_agent_gets_only_the_printers_assigned_to_it()
    {
        var mine = await CreatePrinter();
        var other = AddAgent();
        await using (var context = NewContext())
        {
            await NewService(context).Create(
                _userId,
                _workspaceId,
                new CreatePrinterRequest("Theirs", "octagrid.moonraker", Settings(), new Dictionary<string, string>(), AgentId: other),
                CancellationToken.None);
        }

        await using var verify = NewContext();
        var profile = Assert.Single(await NewService(verify).GetPrinterProfiles(_agentId, CancellationToken.None));
        Assert.Equal(mine.Id, profile.PrinterId);
    }

    [Fact]
    public async Task A_user_outside_the_workspace_cannot_read_a_printers_agent()
    {
        var created = await CreatePrinter();

        await using var context = NewContext();
        var service = NewService(context);
        await Assert.ThrowsAsync<AuthenticationException>(() => service.GetAgentId(Guid.NewGuid(), created.Id, Permission.View, CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(() => service.GetAgentIds(Guid.NewGuid(), [created.Id], Permission.View, CancellationToken.None));
    }

    [Fact]
    public async Task The_agents_of_several_printers_leave_out_those_without_one()
    {
        var assigned = await CreatePrinter();
        AddAgent();
        var unassigned = await CreatePrinter();

        await using var context = NewContext();
        var agents = await NewService(context).GetAgentIds(_userId, [assigned.Id, unassigned.Id, Guid.NewGuid()], Permission.View, CancellationToken.None);

        Assert.Equal(_agentId, Assert.Single(agents, a => a.Key == assigned.Id).Value);
        Assert.Single(agents);
    }

    [Fact]
    public async Task Assigning_a_printer_moves_it_to_the_new_agent_and_announces_the_change()
    {
        var created = await CreatePrinter();
        var other = AddAgent();
        _publisher.Published.Clear();

        await using (var context = NewContext())
        {
            await new AgentsService(context, TestAccess.For(context), _publisher).AssignPrinter(_userId, other, created.Id, CancellationToken.None);
        }

        await using var verify = NewContext();
        Assert.Equal(other, await NewService(verify).GetAgentId(_userId, created.Id, Permission.View, CancellationToken.None));
        var change = Assert.Single(_publisher.Published);
        Assert.Equal(EntityTypes.Printer, change.EntityType);
        Assert.Equal(created.Id, change.EntityId);
    }

    [Fact]
    public async Task A_printer_cannot_be_assigned_to_an_agent_of_another_workspace()
    {
        var created = await CreatePrinter();
        var foreign = AddAgent(AddWorkspace());

        await using var context = NewContext();
        await Assert.ThrowsAsync<ValidationException>(() =>
            new AgentsService(context, TestAccess.For(context), _publisher).AssignPrinter(_userId, foreign, created.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Agents_are_listed_in_the_order_they_were_created()
    {
        var second = AddAgent();

        await using var context = NewContext();
        var agents = await new AgentsService(context, TestAccess.For(context), _publisher).GetList(_userId, _workspaceId, CancellationToken.None);

        Assert.Equal([_agentId, second], agents.Select(a => a.Id));
    }

    [Fact]
    public async Task Removing_an_agent_keeps_its_printers_without_an_agent()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "kept" });

        await using (var context = NewContext())
        {
            context.Agents.Remove(context.Agents.Single(a => a.Id == _agentId));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = NewContext();
        var loaded = await NewService(verify).GetPrinter(_userId, _workspaceId, created.Id, CancellationToken.None);
        Assert.Null(loaded.AgentId);
        Assert.Equal("kept", SavedSecrets(created.Id)["apiKey"]);
    }
}