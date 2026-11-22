using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Application.Events;
using OctaGrid.Application.Secrets;
using OctaGrid.Application.Services;
using OctaGrid.Application.Telemetry;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Application.Tests;

/// <summary>
/// <see cref="EntityChanged"/> messages are derived from saved domain events: nothing is published
/// until the outbox is delivered, and each message carries the version the change was saved with.
/// </summary>
public sealed class EntityChangePublishingTests : IAsyncDisposable
{
    private readonly AgentLinkFixture _fixture = new();
    private readonly RecordingPublisher _published = new();
    private readonly ServiceProvider _services;

    public EntityChangePublishingTests()
    {
        var services = new ServiceCollection();
        services.AddScoped(sp => _fixture.NewContext(sp.GetRequiredService<OutboxDispatcher>()));
        services.AddScoped(sp => TestAccess.For(sp.GetRequiredService<Data.OctaGridContext>()));
        services.AddSingleton<ISecretProtector>(new AesGcmSecretProtector(new FixedKeySource()));
        services.AddSingleton<IMessagePublisher>(_published);
        services.AddScoped<IDomainEventHandler, EntityChangePublisher>();
        services.AddScoped<PrintersService>();
        services.AddScoped<WorkspacesService>();
        services.AddSingleton<OutboxDispatcher>();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _fixture.Dispose();
    }

    private async Task<T> Call<T>(Func<IServiceProvider, Task<T>> call)
    {
        await using var scope = _services.CreateAsyncScope();
        return await call(scope.ServiceProvider);
    }

    private async Task Call(Func<IServiceProvider, Task> call)
    {
        await using var scope = _services.CreateAsyncScope();
        await call(scope.ServiceProvider);
    }

    private Task Deliver() => _services.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync(CancellationToken.None);

    private Task<PrinterResponse> CreatePrinter() => Call(sp => sp.GetRequiredService<PrintersService>().Create(
        _fixture.UserId,
        _fixture.WorkspaceId,
        new CreatePrinterRequest("Voron", "octagrid.moonraker", new Dictionary<string, string> { ["url"] = "10.0.0.1" }, new Dictionary<string, string>(), true),
        CancellationToken.None));

    [Fact]
    public async Task Nothing_is_published_until_the_outbox_is_delivered()
    {
        await CreatePrinter();

        Assert.Empty(_published.Messages);
        await Deliver();
        Assert.Single(_published.Messages);
    }

    [Fact]
    public async Task A_new_printer_is_published_as_upserted_with_its_saved_version()
    {
        var created = await CreatePrinter();

        await Deliver();

        var change = Assert.Single(_published.Messages);
        Assert.Equal(new EntityChanged(EntityTypes.Printer, created.Id, created.Version, ChangeKind.Upserted, _fixture.WorkspaceId), change);
    }

    [Fact]
    public async Task An_edited_printer_is_published_once_with_the_version_after_the_edit()
    {
        var created = await CreatePrinter();
        await Deliver();

        var updated = await Call(sp => sp.GetRequiredService<PrintersService>().Update(
            _fixture.UserId,
            created.Id,
            new UpdatePrinterRequest("Renamed", created.ConnectorId, created.Settings, new Dictionary<string, string> { ["apiKey"] = "new" }, false),
            CancellationToken.None));
        await Deliver();

        var change = _published.Messages[^1];
        Assert.Equal(2, _published.Messages.Count);
        Assert.Equal(updated.Version, change.Version);
        Assert.Equal(ChangeKind.Upserted, change.Kind);
    }

    [Fact]
    public async Task A_deleted_printer_is_published_as_deleted()
    {
        var created = await CreatePrinter();

        await Call(sp => sp.GetRequiredService<PrintersService>().Delete(_fixture.UserId, created.Id, CancellationToken.None));
        await Deliver();

        var change = _published.Messages[^1];
        Assert.Equal(created.Id, change.EntityId);
        Assert.Equal(ChangeKind.Deleted, change.Kind);
    }

    [Fact]
    public async Task A_renamed_workspace_is_published_with_its_saved_version()
    {
        var renamed = await Call(sp => sp.GetRequiredService<WorkspacesService>().Update(
            _fixture.UserId, _fixture.WorkspaceId, new UpdateWorkspaceRequest("Renamed"), CancellationToken.None));

        await Deliver();

        var change = Assert.Single(_published.Messages);
        Assert.Equal(new EntityChanged(EntityTypes.Workspace, _fixture.WorkspaceId, renamed.Version, ChangeKind.Upserted, _fixture.WorkspaceId), change);
    }

    [Fact]
    public async Task Renaming_a_workspace_to_its_current_name_publishes_nothing()
    {
        await Call(sp => sp.GetRequiredService<WorkspacesService>().Update(
            _fixture.UserId, _fixture.WorkspaceId, new UpdateWorkspaceRequest("Test workspace"), CancellationToken.None));

        await Deliver();

        Assert.Empty(_published.Messages);
    }

    [Fact]
    public async Task Saving_a_printer_unchanged_publishes_nothing()
    {
        var created = await Call(sp => sp.GetRequiredService<PrintersService>().Create(
            _fixture.UserId,
            _fixture.WorkspaceId,
            new CreatePrinterRequest("Voron", "octagrid.moonraker", new Dictionary<string, string> { ["url"] = "10.0.0.1" }, new Dictionary<string, string> { ["apiKey"] = "secret" }, true),
            CancellationToken.None));
        await Deliver();

        // The same secret entered again is not a change either, although encrypting it again would give other text.
        await Call(sp => sp.GetRequiredService<PrintersService>().Update(
            _fixture.UserId,
            created.Id,
            new UpdatePrinterRequest(created.Name, created.ConnectorId, new Dictionary<string, string> { ["url"] = "10.0.0.1" }, new Dictionary<string, string> { ["apiKey"] = "secret" }, created.Enabled),
            CancellationToken.None));
        await Call(sp => sp.GetRequiredService<PrintersService>().UpdateIdentity(
            _fixture.UserId, created.Id, new UpdatePrinterIdentityRequest(created.Name), CancellationToken.None));
        await Deliver();

        Assert.Single(_published.Messages);
    }

    [Fact]
    public async Task A_save_with_events_wakes_the_running_dispatcher()
    {
        var waiting = _published.WaitForAsync(1);
        using var stop = new CancellationTokenSource();
        var running = _services.GetRequiredService<OutboxDispatcher>().RunAsync(stop.Token);

        await CreatePrinter();
        await waiting;

        await stop.CancelAsync();
        await running;
        Assert.Single(_published.Messages);
    }

    private sealed class RecordingPublisher : IMessagePublisher
    {
        private readonly List<EntityChanged> _messages = [];
        private readonly List<(int Count, TaskCompletionSource Done)> _waiters = [];

        public IReadOnlyList<EntityChanged> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToList();
                }
            }
        }

        public void Publish(EntityChanged entity)
        {
            lock (_messages)
            {
                _messages.Add(entity);
                foreach (var waiter in _waiters.Where(w => _messages.Count >= w.Count))
                {
                    waiter.Done.TrySetResult();
                }
            }
        }

        public Task WaitForAsync(int count)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_messages)
            {
                if (_messages.Count >= count)
                {
                    return Task.CompletedTask;
                }

                _waiters.Add((count, done));
            }

            // Guards against a hang only; the publisher completes the wait when the message arrives.
            return done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    private sealed class FixedKeySource : ISecretKeySource
    {
        public byte[] GetOrCreateKey() => Enumerable.Range(1, AesGcmSecretProtector.KeySize).Select(i => (byte)i).ToArray();
    }
}
