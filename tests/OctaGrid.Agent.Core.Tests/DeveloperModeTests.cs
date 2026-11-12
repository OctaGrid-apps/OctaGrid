using System.Globalization;
using System.Runtime.CompilerServices;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Agent.Core.Extensions;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Tests;

/// <summary>A connector for the developer-mode tests. It reports the printer as idle as soon as its session starts.</summary>
public sealed class DevConnectorA : IConnector
{
    public const string Id = "acme.dev-sample";

    public ConnectorDescriptor Descriptor { get; } = new(Id, "Dev Sample A", "1.0.0", "Acme", ConnectorCapabilities.None, [], []);

    public IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context) => new DevSession(profile);
}

/// <summary>"The next build" of <see cref="DevConnectorA"/>: same id, different name.</summary>
public sealed class DevConnectorB : IConnector
{
    public ConnectorDescriptor Descriptor { get; } = new(DevConnectorA.Id, "Dev Sample B", "1.0.0", "Acme", ConnectorCapabilities.None, [], []);

    public IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context) => new DevSession(profile);
}

public sealed class DevSession(PrinterProfile profile) : IPrinterSession
{
    public async Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken)
    {
        telemetry.Send(new PrinterTelemetry(profile.PrinterId) { State = UniversalPrinterState.Idle });
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ConnectionTestResult.Ok());

    public Task RequestStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class DeveloperModeTests : IDisposable
{
    private static readonly ExtensionVersion Host = ExtensionVersion.Parse("1.2.0");

    private readonly string _work = Path.Combine(Path.GetTempPath(), "octagrid-dev-" + Guid.NewGuid().ToString("N"));

    private string DevFolder => Path.Combine(_work, "build-output");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An assembly that is still loaded stays locked on Windows.
        }
    }

    private sealed class FixedProfiles(params PrinterProfile[] profiles) : IPrinterProfileSource
    {
        public Task<IReadOnlyList<PrinterProfile>> GetProfilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PrinterProfile>>(profiles);
    }

    /// <summary>Writes what a build leaves behind: the manifest and a copy of this test assembly, which holds the connectors.</summary>
    private void Build(Type entryType, string? hostRange = null)
    {
        Directory.CreateDirectory(DevFolder);
        var source = typeof(DeveloperModeTests).Assembly.Location;
        File.Copy(source, Path.Combine(DevFolder, Path.GetFileName(source)), overwrite: true);
        File.WriteAllText(Path.Combine(DevFolder, ExtensionManifest.FileName), $$"""
            {
              "id": "{{DevConnectorA.Id}}",
              "name": "Dev Sample",
              "version": "1.0.0",
              "publisher": "Acme",
              "kind": "connector",
              "host": "{{hostRange ?? ">=1.0.0 <2.0.0"}}",
              "assembly": "{{Path.GetFileName(source)}}",
              "entryType": "{{entryType.FullName}}"
            }
            """);
    }

    private static ExtensionManager NewManager(ConnectorRegistry? registry = null) =>
        new(registry ?? new ConnectorRegistry(), new InMemoryStateStore(), new ExtensionLoader(Host));

    private static void Reload(ExtensionManager manager)
    {
        manager.BeginDeveloperReload();
        manager.CompleteDeveloperReload();
    }

    private static string NameOf(ExtensionManager manager) => Assert.Single(manager.GetExtensions()).Name;

    // ---- Loading ----

    [Fact]
    public void The_developer_folder_is_loaded_without_asking_and_marked_as_a_developer_extension()
    {
        Build(typeof(DevConnectorA));
        var manager = NewManager();
        manager.SetDeveloperFolder(DevFolder);

        Reload(manager);

        var info = Assert.Single(manager.GetExtensions());
        Assert.Equal(ExtensionState.Loaded, info.State);
        Assert.Equal(ExtensionSource.Development, info.Source);
        Assert.False(info.CanUninstall);
        Assert.Equal("Dev Sample A", info.Name);
        Assert.True(manager.TryGetConnector(DevConnectorA.Id, out _));
    }

    [Fact]
    public void A_folder_with_several_extensions_inside_it_works_too()
    {
        Build(typeof(DevConnectorA));
        var parent = Path.Combine(_work, "all");
        Directory.CreateDirectory(parent);
        Directory.Move(DevFolder, Path.Combine(parent, "one"));
        var manager = NewManager();
        manager.SetDeveloperFolder(parent);

        Reload(manager);

        Assert.Equal(ExtensionState.Loaded, Assert.Single(manager.GetExtensions()).State);
    }

    [Fact]
    public void The_extension_runs_from_a_copy_so_the_next_build_can_overwrite_the_files()
    {
        Build(typeof(DevConnectorA));
        var manager = NewManager();
        manager.SetDeveloperFolder(DevFolder);
        Reload(manager);

        // On Windows a loaded assembly cannot be deleted or replaced; the copy is what is loaded.
        var dll = Path.Combine(DevFolder, Path.GetFileName(typeof(DeveloperModeTests).Assembly.Location));
        File.Delete(dll);

        Assert.False(File.Exists(dll));
        Assert.Equal(ExtensionState.Loaded, Assert.Single(manager.GetExtensions()).State);
    }

    [Fact]
    public void Nothing_is_loaded_without_a_developer_folder()
    {
        var manager = NewManager();

        Reload(manager);

        Assert.Empty(manager.GetExtensions());
        Assert.Null(manager.DeveloperFolder);
    }

    // ---- Reloading ----

    [Fact]
    public void Reloading_after_a_build_loads_the_new_version_in_place_of_the_old_one()
    {
        Build(typeof(DevConnectorA));
        var manager = NewManager();
        manager.SetDeveloperFolder(DevFolder);
        Reload(manager);
        Assert.Equal("Dev Sample A", NameOf(manager));

        Build(typeof(DevConnectorB));
        Reload(manager);

        Assert.Equal("Dev Sample B", NameOf(manager));
        Assert.Equal(ExtensionState.Loaded, Assert.Single(manager.GetExtensions()).State);
    }

    [Fact]
    public void While_a_reload_is_under_way_the_extension_counts_as_off_so_its_sessions_stop()
    {
        Build(typeof(DevConnectorA));
        var manager = NewManager();
        manager.SetDeveloperFolder(DevFolder);
        Reload(manager);

        manager.BeginDeveloperReload();

        Assert.True(manager.IsDisabled(DevConnectorA.Id));
        Assert.Empty(manager.EnabledDescriptors);

        manager.CompleteDeveloperReload();

        Assert.False(manager.IsDisabled(DevConnectorA.Id));
        Assert.Single(manager.EnabledDescriptors);
    }

    [Fact]
    public void A_build_that_breaks_the_extension_shows_the_error_and_the_next_good_build_fixes_it()
    {
        Build(typeof(DevConnectorA));
        var manager = NewManager();
        manager.SetDeveloperFolder(DevFolder);
        Reload(manager);

        File.WriteAllText(Path.Combine(DevFolder, ExtensionManifest.FileName), "not json");
        Reload(manager);

        var broken = Assert.Single(manager.GetExtensions());
        Assert.Equal(ExtensionState.Failed, broken.State);
        Assert.Equal(ExtensionSource.Development, broken.Source);
        Assert.False(manager.TryGetConnector(DevConnectorA.Id, out _));

        Build(typeof(DevConnectorA));
        Reload(manager);

        Assert.Equal(ExtensionState.Loaded, Assert.Single(manager.GetExtensions()).State);
    }

    [Fact]
    public void An_extension_for_another_host_version_is_listed_as_incompatible()
    {
        Build(typeof(DevConnectorA), hostRange: ">=9.0.0");
        var manager = NewManager();
        manager.SetDeveloperFolder(DevFolder);

        Reload(manager);

        Assert.Equal(ExtensionState.Incompatible, Assert.Single(manager.GetExtensions()).State);
    }

    [Fact]
    public void Clearing_the_developer_folder_unloads_its_extensions()
    {
        Build(typeof(DevConnectorA));
        var manager = NewManager();
        manager.SetDeveloperFolder(DevFolder);
        Reload(manager);

        manager.SetDeveloperFolder(null);
        Reload(manager);

        Assert.Empty(manager.GetExtensions());
        Assert.False(manager.TryGetConnector(DevConnectorA.Id, out _));
    }

    [Fact]
    public void A_developer_extension_cannot_replace_an_extension_that_is_already_registered()
    {
        Build(typeof(DevConnectorA));
        var registry = new ConnectorRegistry().Add(new FakeConnector(DevConnectorA.Id, _ => new MinimalSession()));
        var manager = NewManager(registry);
        manager.SetDeveloperFolder(DevFolder);

        Reload(manager);

        Assert.Contains(manager.GetExtensions(), e => e.State == ExtensionState.Failed && e.Source == ExtensionSource.Development);
        Assert.Equal(ExtensionSource.BuiltIn, registry.GetSource(DevConnectorA.Id));
    }

    [Fact]
    public void A_developer_extension_is_not_removed_with_the_uninstall_button()
    {
        Build(typeof(DevConnectorA));
        var manager = new ExtensionManager(new ConnectorRegistry(), new InMemoryStateStore(), new ExtensionLoader(Host), Path.Combine(_work, "extensions"));
        manager.SetDeveloperFolder(DevFolder);
        Reload(manager);

        var result = manager.Uninstall(DevConnectorA.Id);

        Assert.False(result.Success);
        Assert.Contains("developer folder", result.Message);
    }

    // ---- Unloading ----

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndUnload(ExtensionLoader loader, string folder)
    {
        var result = Assert.Single(loader.LoadDevelopment(folder));
        Assert.Equal(ExtensionState.Loaded, result.State);
        _ = result.Connector!.Descriptor;
        var weak = result.Handle!.WeakContext;
        Assert.True(weak.IsAlive);
        result.Handle.Unload();
        return weak;
    }

    [Fact]
    public async Task The_old_version_is_really_unloaded_from_memory()
    {
        Build(typeof(DevConnectorA));

        var weak = LoadAndUnload(new ExtensionLoader(Host), DevFolder);

        for (var i = 0; i < 40 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.False(weak.IsAlive, "The load context of the old version is still in memory.");
    }

    [Fact]
    public void Cleaning_up_removes_old_shadow_copies_but_never_a_fresh_one()
    {
        var root = Path.Combine(Path.GetTempPath(), "OctaGrid-extensions-dev");
        var old = Path.Combine(root, "test-old-" + Guid.NewGuid().ToString("N"));
        var fresh = Path.Combine(root, "test-fresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(fresh);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));

        ExtensionLoader.CleanUpShadowCopies();

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(fresh));
        Directory.Delete(fresh);
    }

    // ---- The worker: sessions and the file watcher ----

    private static (AgentWorker Worker, FakeTelemetry Telemetry, PrinterProfile Printer) NewWorker()
    {
        var printer = new PrinterProfile(Guid.NewGuid(), DevConnectorA.Id, new Dictionary<string, string>());
        var telemetry = new FakeTelemetry();
        var manager = NewManager();
        return (new AgentWorker(telemetry, new FixedProfiles(printer), manager), telemetry, printer);
    }

    private static async Task Eventually(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail(because);
    }

    [Fact]
    public async Task Turning_developer_mode_on_starts_the_printers_that_use_the_extension()
    {
        Build(typeof(DevConnectorA));
        var (worker, telemetry, printer) = NewWorker();
        await worker.ExecuteAsync(CancellationToken.None);
        Assert.DoesNotContain(telemetry.Sent, t => t.PrinterId == printer.PrinterId && t.State == UniversalPrinterState.Idle);

        var result = await worker.SetDeveloperFolderAsync(DevFolder, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, Assert.Single(worker.GetExtensions()).ActivePrinters);
        await Eventually(() => telemetry.Sent.Any(t => t.PrinterId == printer.PrinterId && t.State == UniversalPrinterState.Idle), "The printer never started.");
        await worker.DisposeAsync();
    }

    [Fact]
    public async Task Reloading_restarts_the_session_with_the_new_version()
    {
        Build(typeof(DevConnectorA));
        var (worker, telemetry, printer) = NewWorker();
        await worker.SetDeveloperFolderAsync(DevFolder, CancellationToken.None);
        await Eventually(() => telemetry.Sent.Any(t => t.PrinterId == printer.PrinterId), "The printer never started.");
        var starts = telemetry.Sent.Count(t => t.PrinterId == printer.PrinterId && t.State == UniversalPrinterState.Idle);

        Build(typeof(DevConnectorB));
        await worker.ReloadDeveloperExtensionsAsync(CancellationToken.None);

        Assert.Equal("Dev Sample B", Assert.Single(worker.GetExtensions()).Name);
        Assert.Equal(1, Assert.Single(worker.GetExtensions()).ActivePrinters);
        await Eventually(() => telemetry.Sent.Count(t => t.PrinterId == printer.PrinterId && t.State == UniversalPrinterState.Idle) > starts, "The session never restarted.");
        await worker.DisposeAsync();
    }

    [Fact]
    public async Task A_folder_that_does_not_exist_is_refused()
    {
        var (worker, _, _) = NewWorker();

        var result = await worker.SetDeveloperFolderAsync(Path.Combine(_work, "nowhere"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(worker.DeveloperFolder);
    }

    [Fact]
    public async Task A_new_build_in_the_developer_folder_is_picked_up_without_asking()
    {
        Build(typeof(DevConnectorA));
        var (worker, _, _) = NewWorker();
        await worker.SetDeveloperFolderAsync(DevFolder, CancellationToken.None);
        Assert.Equal("Dev Sample A", Assert.Single(worker.GetExtensions()).Name);

        Build(typeof(DevConnectorB));

        await Eventually(() => worker.GetExtensions().SingleOrDefault()?.Name == "Dev Sample B", "The watcher never reloaded the extension.");
        await worker.DisposeAsync();
    }

    [Fact]
    public async Task Turning_developer_mode_off_stops_the_printers_and_the_watching()
    {
        Build(typeof(DevConnectorA));
        var (worker, _, _) = NewWorker();
        await worker.SetDeveloperFolderAsync(DevFolder, CancellationToken.None);

        await worker.SetDeveloperFolderAsync(null, CancellationToken.None);
        Build(typeof(DevConnectorB));
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        Assert.Empty(worker.GetExtensions());
        Assert.Null(worker.DeveloperFolder);
        await worker.DisposeAsync();
    }

    // ---- The watcher ----

    [Fact]
    public async Task The_watcher_reports_a_burst_of_writes_once_and_ignores_other_files()
    {
        Directory.CreateDirectory(DevFolder);
        var calls = 0;
        using var watcher = new DeveloperExtensionWatcher(DevFolder, () => Interlocked.Increment(ref calls), TimeSpan.FromMilliseconds(150));

        File.WriteAllText(Path.Combine(DevFolder, "notes.pdb"), "x");
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Equal(0, calls);

        for (var i = 0; i < 5; i++)
        {
            File.WriteAllText(Path.Combine(DevFolder, "Extension.dll"), i.ToString(CultureInfo.InvariantCulture));
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        await Eventually(() => calls >= 1, "The watcher never reported the change.");
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
    }
}
