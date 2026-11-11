using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OctaGrid.Application.Secrets;
using OctaGrid.Application.Services;
using OctaGrid.Application.Telemetry;
using OctaGrid.Data.Abstractions;
using OctaGrid.Data.Abstractions.Models;
using OctaGrid.Protocol.Exceptions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Application.Tests;

/// <summary>Runs the printers service against a real (in-memory) SQLite database.</summary>
public sealed class PrintersServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly RecordingPublisher _publisher = new();
    private readonly InMemorySecretStore _secrets = new();

    public PrintersServiceTests()
    {
        _connection.Open();
        using var context = NewContext();
        context.Database.EnsureCreated();

        context.Users.Add(new User { Id = _userId, Name = "Test", Email = "test@example.com", PasswordHash = "x" });
        context.Workspaces.Add(new Workspace { Id = _workspaceId, Name = "Test workspace" });
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

    private sealed class InMemorySecretStore : IPrinterSecretStore
    {
        private readonly Dictionary<Guid, Dictionary<string, string>> _byPrinter = [];

        public IReadOnlyDictionary<string, string> Saved(Guid printerId) =>
            _byPrinter.TryGetValue(printerId, out var secrets) ? secrets : new Dictionary<string, string>();

        public Task<IReadOnlyDictionary<string, string>> GetAsync(Guid printerId, CancellationToken cancellationToken) =>
            Task.FromResult(Saved(printerId));

        public Task SetAsync(Guid printerId, IReadOnlyDictionary<string, string> secrets, CancellationToken cancellationToken)
        {
            if (secrets.Count == 0)
            {
                _byPrinter.Remove(printerId);
            }
            else
            {
                _byPrinter[printerId] = new Dictionary<string, string>(secrets);
            }

            return Task.CompletedTask;
        }
    }

    private OctaGridContext NewContext() =>
        new(new DbContextOptionsBuilder<OctaGridContext>().UseSqlite(_connection).Options);

    private PrintersService NewService(OctaGridContext context) => new(context, _publisher, _secrets);

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
        Assert.Equal("secret", _secrets.Saved(created.Id)["apiKey"]);
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
        Assert.Contains("apiKey", row);
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
        Assert.Equal("t\"o'k\\en", _secrets.Saved(created.Id)["token"]);
    }

    [Fact]
    public async Task A_printer_without_secrets_has_no_secret_keys()
    {
        var created = await CreatePrinter(secrets: []);

        await using var context = NewContext();
        var loaded = await NewService(context).GetPrinter(_userId, _workspaceId, created.Id, CancellationToken.None);

        Assert.Empty(loaded.SecretKeys);
        Assert.Empty(_secrets.Saved(created.Id));
    }

    [Fact]
    public async Task An_empty_secret_is_not_saved()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "" });

        Assert.Empty(created.SecretKeys);
        Assert.Empty(_secrets.Saved(created.Id));
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
        Assert.Equal("old", _secrets.Saved(created.Id)["apiKey"]);
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
        Assert.Equal("new", Assert.Single(_secrets.Saved(created.Id)).Value);
    }

    [Fact]
    public async Task Delete_removes_the_saved_secrets()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "old" });

        await using var context = NewContext();
        await NewService(context).Delete(_userId, created.Id, CancellationToken.None);

        Assert.Empty(_secrets.Saved(created.Id));
    }

    [Fact]
    public async Task Saved_secrets_fill_the_ones_left_empty_when_testing_a_connection()
    {
        var created = await CreatePrinter(secrets: new Dictionary<string, string> { ["apiKey"] = "saved", ["token"] = "saved-token" });

        await using var context = NewContext();
        var settings = await NewService(context).AddSavedSecrets(
            _userId,
            created.Id,
            new Dictionary<string, string> { ["url"] = "10.0.0.9", ["apiKey"] = "", ["token"] = "typed" },
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
        Assert.Empty(_secrets.Saved(created.Id));
    }

    [Fact]
    public async Task Printer_profiles_merge_settings_and_secrets_for_enabled_printers_only()
    {
        var on = await CreatePrinter(Settings("192.168.1.5", "01P"), new Dictionary<string, string> { ["apiKey"] = "123456" });
        await CreatePrinter(enabled: false);

        await using var context = NewContext();
        var profiles = await NewService(context).GetPrinterProfiles(_userId, _workspaceId, CancellationToken.None);

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
        var profile = Assert.Single(await NewService(context).GetPrinterProfiles(_userId, _workspaceId, CancellationToken.None));

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
}
