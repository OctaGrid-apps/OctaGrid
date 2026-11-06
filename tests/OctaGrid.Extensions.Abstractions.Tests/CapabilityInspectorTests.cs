using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Extensions.Abstractions.Tests;

public class CapabilityInspectorTests
{
    private static ConnectorDescriptor Descriptor(ConnectorCapabilities capabilities) => new(
        "acme.test", "Test", "1.0.0", "Acme", capabilities, [], []);

    [Fact]
    public void A_bare_session_has_no_capabilities() =>
        Assert.Equal(ConnectorCapabilities.None, CapabilityInspector.Detect(new BareSession()));

    [Fact]
    public void Detects_each_optional_interface()
    {
        Assert.Equal(ConnectorCapabilities.PrintControl, CapabilityInspector.Detect(new ControlSession()));
        Assert.Equal(ConnectorCapabilities.Files, CapabilityInspector.Detect(new FilesSession()));
        Assert.Equal(ConnectorCapabilities.Thumbnails, CapabilityInspector.Detect(new ThumbnailSession()));
        Assert.Equal(ConnectorCapabilities.CustomCommands, CapabilityInspector.Detect(new CommandSession()));
    }

    [Fact]
    public void Combines_all_interfaces_a_session_implements()
    {
        var expected = ConnectorCapabilities.PrintControl | ConnectorCapabilities.Files
            | ConnectorCapabilities.Thumbnails | ConnectorCapabilities.CustomCommands;

        Assert.Equal(expected, CapabilityInspector.Detect(new FullSession()));
    }

    [Fact]
    public void Discovery_comes_from_the_connector_not_the_session()
    {
        var connector = new DiscoveringConnector(Descriptor(ConnectorCapabilities.Discovery));

        Assert.Equal(ConnectorCapabilities.None, CapabilityInspector.Detect(new BareSession()));
        Assert.Equal(ConnectorCapabilities.Discovery, CapabilityInspector.Detect(connector, new BareSession()));
    }

    [Fact]
    public void No_mismatch_when_declaration_matches()
    {
        var connector = new TestConnector(Descriptor(ConnectorCapabilities.PrintControl | ConnectorCapabilities.Files));

        Assert.Null(CapabilityInspector.FindMismatch(connector, new ControlAndFilesSession()));
    }

    [Fact]
    public void Reports_capabilities_declared_but_not_implemented()
    {
        var connector = new TestConnector(Descriptor(ConnectorCapabilities.PrintControl | ConnectorCapabilities.Files));

        var message = CapabilityInspector.FindMismatch(connector, new ControlSession());

        Assert.NotNull(message);
        Assert.Contains("declared but not implemented: Files", message);
        Assert.Contains("acme.test", message);
    }

    [Fact]
    public void Reports_capabilities_implemented_but_not_declared()
    {
        var connector = new TestConnector(Descriptor(ConnectorCapabilities.None));

        var message = CapabilityInspector.FindMismatch(connector, new ThumbnailSession());

        Assert.NotNull(message);
        Assert.Contains("implemented but not declared: Thumbnails", message);
    }

    [Fact]
    public void Throws_for_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => CapabilityInspector.Detect(null!));
        Assert.Throws<ArgumentNullException>(() => CapabilityInspector.Detect(null!, new BareSession()));
    }

    // --- test doubles ---------------------------------------------------------------------------

    private class BareSession : IPrinterSession
    {
        public Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConnectionTestResult.Ok());

        public Task RequestStateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ControlSession : BareSession, IPrintControl
    {
        public Task StartPrintAsync(string fileName, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PausePrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumePrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopPrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private class FilesSession : BareSession, IFileStorage
    {
        public Task<IReadOnlyList<PrintMetadata>> ListFilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PrintMetadata>>([]);

        public Task UploadFileAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteFileAsync(string fileName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ThumbnailSession : BareSession, IThumbnails
    {
        public Task<PrintThumbnail?> GetThumbnailAsync(string fileName, CancellationToken cancellationToken) =>
            Task.FromResult<PrintThumbnail?>(null);
    }

    private sealed class CommandSession : BareSession, ICustomCommands
    {
        public Task<CommandResult> ExecuteAsync(
            string commandId, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(CommandResult.Ok());
    }

    private sealed class ControlAndFilesSession : FilesSession, IPrintControl
    {
        public Task StartPrintAsync(string fileName, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PausePrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumePrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopPrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FullSession : FilesSession, IPrintControl, IThumbnails, ICustomCommands
    {
        public Task StartPrintAsync(string fileName, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PausePrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumePrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopPrintAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PrintThumbnail?> GetThumbnailAsync(string fileName, CancellationToken cancellationToken) =>
            Task.FromResult<PrintThumbnail?>(null);

        public Task<CommandResult> ExecuteAsync(
            string commandId, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(CommandResult.Ok());
    }

    private class TestConnector(ConnectorDescriptor descriptor) : IConnector
    {
        public ConnectorDescriptor Descriptor { get; } = descriptor;

        public IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context) => new BareSession();
    }

    private sealed class DiscoveringConnector(ConnectorDescriptor descriptor) : TestConnector(descriptor), IPrinterDiscovery
    {
        public async IAsyncEnumerable<DiscoveredPrinter> ScanAsync(
            DiscoveryOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
