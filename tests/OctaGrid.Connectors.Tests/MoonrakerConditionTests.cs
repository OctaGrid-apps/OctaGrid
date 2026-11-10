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

    private sealed class RecordingConsole : IConsoleSink
    {
        public List<string> Lines { get; } = [];

        public void WriteLine(string text) => Lines.Add(text);
    }

    [Fact]
    public void Gcode_console_output_goes_to_the_console_when_the_host_keeps_one()
    {
        var console = new RecordingConsole();
        var sink = new RecordingSink();
        var listener = new MoonrakerListener(Guid.NewGuid(), sink, console: console);

        listener.OnGcodeResponse(Json("\"ok\""));
        listener.OnGcodeResponse(Json("\"// Unknown command\""));

        Assert.Equal(["ok", "// Unknown command"], console.Lines);
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void A_gcode_response_that_is_not_text_is_ignored_by_the_console()
    {
        var console = new RecordingConsole();
        var listener = new MoonrakerListener(Guid.NewGuid(), new RecordingSink(), console: console);

        listener.OnGcodeResponse(Json("{\"not\":\"text\"}"));
        listener.OnGcodeResponse(Json("null"));

        Assert.Empty(console.Lines);
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

    // What a Klipper simulator returned after a print that failed because the axes were not homed. Klipper itself is
    // fine (webhooks is ready); the reason is only in print_stats.message.
    private const string FailedPrintStatus = """
        {"eventtime":1650.9,"status":{
          "webhooks":{"state":"ready","state_message":"Printer is ready"},
          "print_stats":{"filename":"skull.gcode","total_duration":0.02,"print_duration":0.0,"filament_used":0.0,
            "state":"error","message":"Must home axis first: 0.000 0.000 0.200 [-1.000]",
            "info":{"total_layer":null,"current_layer":null}}}}
        """;

    [Fact]
    public void A_failed_print_reports_why_it_failed_although_klipper_itself_is_ready()
    {
        var (listener, sink) = Create();

        listener.OnStatusUpdate(Json(FailedPrintStatus), 1);

        var telemetry = Assert.Single(sink.Sent);
        Assert.Equal(UniversalPrinterState.Failed, telemetry.State);
        var condition = Assert.Single(telemetry.Conditions);
        Assert.Equal("print.failed", condition.Code);
        Assert.Equal(PrinterSeverity.Warning, condition.Severity);
        Assert.Equal("Must home axis first: 0.000 0.000 0.200 [-1.000]", condition.Message);
    }

    [Theory]
    [InlineData("""{"print_stats":{"state":"printing","message":""}}""")]
    [InlineData("""{"print_stats":{"state":"standby","message":null}}""")]
    public void The_reason_goes_away_when_the_printer_no_longer_reports_the_failure(string update)
    {
        var (listener, sink) = Create();
        listener.OnStatusUpdate(Json(FailedPrintStatus), 1);

        listener.OnStatusUpdate(Json(update), 2);

        Assert.Equal(2, sink.Sent.Count);
        Assert.Empty(sink.Sent[1].Conditions);
    }

    [Fact]
    public void A_shutdown_and_a_failed_print_are_both_reported_most_serious_first()
    {
        var (listener, sink) = Create();

        listener.OnStatusUpdate(
            Json("""{"webhooks":{"state":"shutdown","state_message":"MCU shutdown"},"print_stats":{"state":"error","message":"Print aborted"}}"""), 1);

        var codes = Assert.Single(sink.Sent).Conditions.Select(c => c.Code);
        Assert.Equal(["klippy.state", "print.failed"], codes);
    }

    [Fact]
    public void An_update_without_webhooks_or_print_stats_keeps_the_conditions_in_the_snapshot()
    {
        var (listener, sink) = Create();
        listener.OnStatusUpdate(Json(FailedPrintStatus), 1);

        listener.OnStatusUpdate(Json("""{"extruder":{"temperature":30.0}}"""), 2);

        Assert.Equal(2, sink.Sent.Count);
        Assert.Equal("print.failed", Assert.Single(sink.Sent[1].Conditions).Code);
    }

    [Fact]
    public void A_temperature_that_has_not_changed_is_not_sent_again()
    {
        var (listener, sink) = Create();
        var report = Json("""{"extruder":{"temperature":200.0,"target":210.0}}""");

        listener.OnStatusUpdate(report, 1);
        listener.OnStatusUpdate(report, 2);

        Assert.Single(sink.Sent);
    }

    [Fact]
    public void A_missing_value_is_not_a_change_even_when_the_current_one_is_zero()
    {
        var (listener, sink) = Create();
        listener.OnStatusUpdate(Json("""{"extruder":{"temperature":25.0,"target":0.0}}"""), 1);

        // Moonraker sends only the fields that changed, so the target is simply not in this one.
        listener.OnStatusUpdate(Json("""{"extruder":{"temperature":25.0}}"""), 2);

        Assert.Single(sink.Sent);
    }

    [Fact]
    public void Asking_for_the_state_sends_it_even_when_nothing_changed()
    {
        var (listener, sink) = Create();
        var status = Json(FailedPrintStatus);

        listener.OnStatusQueried(status);
        listener.OnStatusQueried(status);

        Assert.Equal(2, sink.Sent.Count);
        Assert.Equal(UniversalPrinterState.Failed, sink.Sent[1].State);
    }

    [Fact]
    public void An_answer_that_is_not_a_status_is_not_sent()
    {
        var (listener, sink) = Create();

        listener.OnStatusQueried(Json("\"unexpected\""));

        Assert.Empty(sink.Sent);
    }

    [Fact]
    public void The_subscription_asks_for_both_reasons()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(MoonrakerClient.SubscribeParams));
        var objects = document.RootElement.GetProperty("objects");

        Assert.Contains("state_message", objects.GetProperty("webhooks").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("message", objects.GetProperty("print_stats").EnumerateArray().Select(e => e.GetString()));
    }
}
