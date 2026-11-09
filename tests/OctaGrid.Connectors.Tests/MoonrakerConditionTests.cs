using System.Text.Json;
using OctaGrid.Connectors.Moonraker;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Connectors.Tests;

public class MoonrakerConditionTests
{
    private sealed class RecordingSink : ITelemetrySink
    {
        public List<PrinterTelemetry> Sent { get; } = [];

        public void Send(PrinterTelemetry telemetry) => Sent.Add(telemetry);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static (MoonrakerListener Listener, RecordingSink Sink) Create()
    {
        var sink = new RecordingSink();
        return (new MoonrakerListener(Guid.NewGuid(), sink), sink);
    }

    [Fact]
    public void Gcode_console_output_is_not_reported_as_printer_state()
    {
        var (listener, sink) = Create();

        listener.OnGcodeResponse(Json("\"ok\""));
        listener.OnGcodeResponse(Json("\"// Unknown command\""));

        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void A_shutdown_reason_is_an_error_condition()
    {
        var (listener, sink) = Create();

        listener.OnStatusUpdate(
            Json("""{"webhooks":{"state":"shutdown","state_message":"Heater extruder not heating at expected rate"}}"""), 1);

        var condition = Assert.Single(Assert.Single(sink.Sent).Conditions);
        Assert.Equal("klippy.state", condition.Code);
        Assert.Equal(PrinterSeverity.Error, condition.Severity);
        Assert.Equal("Heater extruder not heating at expected rate", condition.Message);
    }

    [Fact]
    public void A_ready_printer_has_no_condition_and_a_recovery_clears_the_old_one()
    {
        var (listener, sink) = Create();

        listener.OnStatusUpdate(Json("""{"webhooks":{"state":"shutdown","state_message":"MCU shutdown"}}"""), 1);
        listener.OnStatusUpdate(Json("""{"webhooks":{"state":"ready","state_message":"Printer is ready"}}"""), 2);

        Assert.Equal(2, sink.Sent.Count);
        Assert.Empty(sink.Sent[1].Conditions);
    }

    [Fact]
    public void A_sent_snapshot_does_not_change_when_the_printer_reports_again()
    {
        var (listener, sink) = Create();

        listener.OnStatusUpdate(Json("""{"extruder":{"temperature":100.0}}"""), 1);
        listener.OnStatusUpdate(Json("""{"extruder":{"temperature":200.0}}"""), 2);

        Assert.Equal(100.0, sink.Sent[0].NozzleTemperature);
        Assert.Equal(200.0, sink.Sent[1].NozzleTemperature);
    }

    [Fact]
    public void Repeating_the_same_webhooks_report_does_not_send_again()
    {
        var (listener, sink) = Create();
        var report = Json("""{"webhooks":{"state":"shutdown","state_message":"MCU shutdown"}}""");

        listener.OnStatusUpdate(report, 1);
        listener.OnStatusUpdate(report, 2);

        Assert.Single(sink.Sent);
    }
}
