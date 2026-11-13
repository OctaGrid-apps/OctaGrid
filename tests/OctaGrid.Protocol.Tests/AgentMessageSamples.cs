using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.AgentLink.Notifications;
using OctaGrid.Protocol.AgentLink.Replies;
using OctaGrid.Protocol.AgentLink.Requests;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Protocol.Tests;

/// <summary>
/// One filled-in instance of every agent message. A test checks that no message type is missing here, so a
/// new message cannot be added without also being serialized in the tests.
/// </summary>
internal static class AgentMessageSamples
{
    private static readonly Guid PrinterId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid UploadId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid StreamId = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly DateTimeOffset At = new(2026, 11, 12, 10, 30, 0, TimeSpan.Zero);

    private static readonly ConnectorDescriptor Connector = new(
        "acme.printer",
        "Acme printer",
        "1.2.0",
        "Acme",
        ConnectorCapabilities.PrintControl | ConnectorCapabilities.Files,
        [new SettingField("url", "Address", SettingFieldType.Url), new SettingField("apiKey", "API key", Required: false, Secret: true)],
        [new CustomCommandDescriptor("home", "Home", "Homes all axes", CustomCommandRisk.Medium, [])]);

    public static IReadOnlyList<AgentRequest> Requests { get; } =
    [
        new StartPrintRequest(PrinterId, "benchy.gcode"),
        new PausePrintRequest(PrinterId),
        new ResumePrintRequest(PrinterId),
        new StopPrintRequest(PrinterId),
        new ListPrinterFilesRequest(PrinterId),
        new GetPrintThumbnailRequest(PrinterId, "benchy.gcode"),
        new DeletePrinterFileRequest(PrinterId, "benchy.gcode"),
        new StorePrinterFileRequest(PrinterId, UploadId, "benchy.gcode"),
        new GetPrinterConsoleRequest(PrinterId, 41, 200),
        new SendConsoleCommandRequest(PrinterId, "M105"),
        new GetPrinterEventsRequest(PrinterId, null, 100),
        new RequestPrinterStateRequest([PrinterId]),
        new ApplyPrinterProfilesRequest(
            [new PrinterProfile(PrinterId, "acme.printer", new Dictionary<string, string> { ["url"] = "10.0.0.5", ["apiKey"] = "k" })]),
        new TestConnectionRequest("acme.printer", new Dictionary<string, string> { ["url"] = "10.0.0.5" }),
        new GetConnectorsRequest(),
        new GetExtensionsRequest(),
        new SetExtensionEnabledRequest("acme.printer", false),
        new InstallExtensionRequest(UploadId),
        new ApproveExtensionRequest("acme.printer"),
        new UninstallExtensionRequest("acme.printer"),
        new GetDeveloperFolderRequest(),
        new SetDeveloperFolderRequest("/home/dev/ext"),
        new ReloadDeveloperExtensionsRequest(),
        new BeginUploadRequest(1024),
        new UploadChunkRequest(UploadId, 0, [1, 2, 3]),
        new AbortUploadRequest(UploadId),
    ];

    public static IReadOnlyList<AgentReply> Replies { get; } =
    [
        new AgentAck(),
        new PrinterFilesReply([new PrintMetadata("benchy.gcode", 2048, "PLA", 0.4, 3600)]),
        new PrintThumbnailReply(new PrintThumbnail("image/png", [9, 8, 7])),
        new PrinterConsoleReply(new ConsolePage([new ConsoleLine(42, At, "ok")], 42, false, StreamId)),
        new PrinterEventsReply(
            new PrinterEventPage([new PrinterEvent(7, At, PrinterSeverity.Warning, "connection.failed", "No answer")], 7, true, StreamId)),
        new ConnectionTestReply(ConnectionTestResult.Fail("Timed out")),
        new ConnectorsReply([Connector]),
        new ExtensionsReply(
        [
            new ExtensionInfo(
                "acme.printer", "Acme printer", "1.2.0", "Acme", "connector", ExtensionSource.UserFolder, ExtensionState.Loaded,
                null, ConnectorCapabilities.Files, CanUninstall: true, ActivePrinters: 2, Permissions: ["network"]),
        ]),
        new ExtensionChangeReply(ExtensionInstallResult.Ok("acme.printer", "Installed")),
        new DeveloperFolderReply(null),
        new UploadStartedReply(UploadId, 65536),
    ];

    public static IReadOnlyList<AgentNotification> Notifications { get; } =
    [
        new PrinterTelemetryReported(new PrinterTelemetry(PrinterId)
        {
            NozzleTemperature = 210.5,
            State = UniversalPrinterState.Printing,
            Conditions = [new PrinterCondition("filament.low", PrinterSeverity.Warning, "Filament is running low")],
            PrintStatus = new PrintStatus { FileName = "benchy.gcode", Progress = 0.42 },
        }),
        new PrinterEventRecorded(PrinterId, StreamId, new PrinterEvent(8, At, PrinterSeverity.Error, "print.failed", "Print failed")),
        new PrinterConsoleLineRecorded(PrinterId, StreamId, new ConsoleLine(43, At, "T:210.5 /210.0")),
        new ConnectorsChanged([Connector]),
    ];
}
