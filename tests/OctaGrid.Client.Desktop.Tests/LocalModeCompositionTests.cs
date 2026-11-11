using Microsoft.Extensions.DependencyInjection;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Local;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Desktop.Tests;

/// <summary>
/// Checks the lifetimes Local mode registers. Nothing is resolved, so no database, extension folder or
/// credential store is touched.
/// </summary>
public sealed class LocalModeCompositionTests
{
    [Fact]
    public void Local_mode_registrations_pass_scope_validation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUserAccessor, FakeCurrentUser>();
        services.AddSingleton<IClientMessageHandler, FakeMessageHandler>();
        services.AddOctoGridClientLocal();

        // Fails when a singleton takes the scoped DbContext (or a service that uses it) in its constructor.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        Assert.NotNull(provider);
    }

    private sealed class FakeCurrentUser : ICurrentUserAccessor
    {
        private Guid? _userId;

        public void Clear() => _userId = null;

        public Guid? GetCurrentUserId() => _userId;

        public void SetCurrentUserId(Guid userId) => _userId = userId;
    }

    private sealed class FakeMessageHandler : IClientMessageHandler
    {
        public void Handle(EntityChanged entity)
        {
        }

        public void Handle(PrinterTelemetry telemetryMessage)
        {
        }

        public void Handle(PrinterEventRecorded recorded)
        {
        }

        public void Handle(PrinterConsoleLineRecorded recorded)
        {
        }
    }
}
