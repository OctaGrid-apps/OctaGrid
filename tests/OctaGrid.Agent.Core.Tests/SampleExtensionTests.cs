using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Agent.Core.Events;
using OctaGrid.Agent.Core.Extensions;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Tests;

/// <summary>
/// Installs the real sample extension from <c>samples/</c> the way a user does: through the manager, from the package
/// folder and from the .octaext file, with the real host version. Catches a manifest that no longer fits the host.
/// </summary>
public sealed class SampleExtensionTests : IDisposable
{
    private const string SampleId = "octagrid-samples.demo-printer";
    private const string PackageFolderName = SampleId;
    private const string PackageFileName = "OctaGrid.Extensions.Sample.octaext";

    private readonly string _work = Path.Combine(Path.GetTempPath(), "octagrid-sample-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Loaded assemblies stay locked on Windows until the process ends.
        }
    }

    private static string SampleOutput()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OctaGrid.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        // Same configuration as this test run when it exists (Debug or Release), otherwise whichever was built.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var bin = Path.Combine(directory!.FullName, "samples", "OctaGrid.Extensions.Sample", "bin");
        var candidates = new[] { Path.Combine(bin, configuration, "net10.0") }
            .Concat(Directory.GetDirectories(bin).Select(d => Path.Combine(d, "net10.0")));
        return candidates.First(c => Directory.Exists(Path.Combine(c, "package", PackageFolderName)));
    }

    private ExtensionManager NewManager() =>
        new(new ConnectorRegistry(), new InMemoryStateStore(), new ExtensionLoader(), Path.Combine(_work, "extensions"));

    [Theory]
    [InlineData(PackageFolderName)]
    [InlineData(PackageFileName)]
    public void The_sample_installs_waits_for_approval_and_then_runs(string packageName)
    {
        var manager = NewManager();

        var installed = manager.Install(Path.Combine(SampleOutput(), "package", packageName));

        Assert.True(installed.Success, installed.Message);
        var pending = Assert.Single(manager.GetExtensions());
        Assert.Equal(SampleId, pending.Id);
        Assert.Equal(ExtensionState.NeedsApproval, pending.State);
        Assert.Equal("OctaGrid Samples", pending.Publisher);

        var approved = manager.Approve(SampleId);

        Assert.True(approved.Success, approved.Message);
        var running = Assert.Single(manager.GetExtensions());
        Assert.Equal(ExtensionState.Loaded, running.State);
        Assert.Equal(ExtensionSource.UserFolder, running.Source);
        Assert.Equal(
            ConnectorCapabilities.PrintControl | ConnectorCapabilities.Files | ConnectorCapabilities.CustomCommands
                | ConnectorCapabilities.Console | ConnectorCapabilities.ConsoleCommands,
            running.Capabilities);
    }

    [Fact]
    public async Task The_sample_declares_what_it_implements_and_simulates_a_print()
    {
        var manager = NewManager();
        manager.Install(Path.Combine(SampleOutput(), "package", PackageFolderName));
        Assert.True(manager.Approve(SampleId).Success);
        manager.TryGetConnector(SampleId, out var connector);

        var profile = new PrinterProfile(Guid.NewGuid(), SampleId, new Dictionary<string, string> { ["speed"] = "Fast" });
        await using var session = connector!.CreateSession(profile, new ConnectorContext(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Path.Combine(_work, "data")));

        Assert.Null(CapabilityInspector.FindMismatch(connector, session));

        var telemetry = new FakeTelemetry();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var run = session.RunAsync(telemetry, cts.Token);
        var control = (IPrintControl)session;

        await control.StartPrintAsync("benchy.gcode", cts.Token);
        await WaitFor(telemetry, UniversalPrinterState.Printing);
        await control.PausePrintAsync(cts.Token);
        await WaitFor(telemetry, UniversalPrinterState.Paused);
        await control.ResumePrintAsync(cts.Token);
        await WaitFor(telemetry, UniversalPrinterState.Completed);

        var last = telemetry.Sent.Last(t => t.State == UniversalPrinterState.Completed);
        Assert.Equal("benchy.gcode", last.PrintStatus.FileName);
        Assert.Equal(1.0, last.PrintStatus.Progress);
        Assert.Contains(telemetry.Sent, t => t.NozzleTemperature > 150);

        await WaitFor(telemetry, UniversalPrinterState.Idle, after: telemetry.Sent.Count);

        cts.Cancel();
        await run;

        // Feed what the sample sent to the host's log, as the agent does, to see the history it produces.
        var log = new PrinterEventLog();
        foreach (var snapshot in telemetry.Sent)
        {
            log.Observe(snapshot);
        }

        var history = log.Get(profile.PrinterId, null, 50).Events;
        var messages = history.Select(e => e.Message).ToList();
        var warning = history.Single(e => e.Code == "filament.low" && e.Severity == PrinterSeverity.Warning);
        var resolved = history.Single(e => e.Message == "Resolved: Filament is running low.");
        Assert.Equal("Filament is running low.", warning.Message);
        Assert.True(warning.Sequence < resolved.Sequence);
        Assert.Contains("State changed to Printing.", messages);
        Assert.Contains("State changed to Completed.", messages);
    }

    [Fact]
    public async Task The_sample_lists_files_and_answers_its_custom_command()
    {
        var manager = NewManager();
        manager.Install(Path.Combine(SampleOutput(), "package", PackageFolderName));
        manager.Approve(SampleId);
        manager.TryGetConnector(SampleId, out var connector);
        await using var session = connector!.CreateSession(
            new PrinterProfile(Guid.NewGuid(), SampleId, new Dictionary<string, string>()),
            new ConnectorContext(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Path.Combine(_work, "data")));

        var files = await ((IFileStorage)session).ListFilesAsync(CancellationToken.None);
        var hello = await ((ICustomCommands)session).ExecuteAsync("hello", new Dictionary<string, string>(), CancellationToken.None);

        Assert.Contains(files, f => f.FileName == "benchy.gcode");
        Assert.True(hello.Success);
        Assert.Contains("sample extension", hello.Message);
    }

    [Fact]
    public void The_sample_loads_in_developer_mode_straight_from_its_build_output()
    {
        var manager = NewManager();
        manager.SetDeveloperFolder(Path.Combine(SampleOutput(), "package", PackageFolderName));

        manager.BeginDeveloperReload();
        manager.CompleteDeveloperReload();

        var info = Assert.Single(manager.GetExtensions());
        Assert.Equal(SampleId, info.Id);
        if (info.State != ExtensionState.Loaded)
        {
            Assert.Fail($"The sample is {info.State}: {info.StateMessage}");
        }

        Assert.Equal(ExtensionSource.Development, info.Source);
    }

    private static async Task WaitFor(FakeTelemetry telemetry, UniversalPrinterState state, int after = 0)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (telemetry.Sent.Skip(after).Any(t => t.State == state))
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"The printer never reached {state}.");
    }
}
