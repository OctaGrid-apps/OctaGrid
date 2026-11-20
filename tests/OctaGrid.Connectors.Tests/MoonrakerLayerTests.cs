using System.Text.Json;
using OctaGrid.Connectors.Moonraker;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Connectors.Tests;

public class MoonrakerLayerTests
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
    public void Layers_come_from_file_metadata_and_height_when_the_print_reports_none()
    {
        var (listener, sink) = Create();
        listener.OnStatusQueried(Json("""{"status":{"print_stats":{"filename":"a.gcode","state":"printing","info":{"total_layer":null,"current_layer":null}}}}"""));
        listener.OnFileMetadata("a.gcode", null, new LayerMetadata(LayerCount: 100, LayerHeight: 0.2, FirstLayerHeight: 0.2));

        listener.OnStatusUpdate(Json("""{"gcode_move":{"gcode_position":[10,10,5.0,0]}}"""), 1);

        var last = sink.Sent[^1].PrintStatus;
        Assert.Equal(100, last.LayersCount);
        Assert.Equal(25, last.CurrentLayer);
    }

    [Fact]
    public void Total_is_worked_out_from_object_height_when_the_layer_count_is_missing()
    {
        var (listener, sink) = Create();
        listener.OnStatusQueried(Json("""{"status":{"print_stats":{"filename":"a.gcode","state":"printing"}}}"""));

        listener.OnFileMetadata("a.gcode", null, new LayerMetadata(LayerHeight: 0.2, FirstLayerHeight: 0.3, ObjectHeight: 20.1));

        Assert.Equal(100, sink.Sent[^1].PrintStatus.LayersCount);
    }

    [Fact]
    public void Layers_reported_by_the_print_win_over_the_fallback()
    {
        var (listener, sink) = Create();
        listener.OnStatusQueried(Json("""{"status":{"print_stats":{"filename":"a.gcode","state":"printing","info":{"total_layer":50,"current_layer":3}}}}"""));

        listener.OnFileMetadata("a.gcode", null, new LayerMetadata(LayerCount: 100, LayerHeight: 0.2));
        listener.OnStatusUpdate(Json("""{"gcode_move":{"gcode_position":[0,0,5.0,0]}}"""), 1);

        var last = sink.Sent[^1].PrintStatus;
        Assert.Equal(50, last.LayersCount);
        Assert.Equal(3, last.CurrentLayer);
    }

    [Fact]
    public void A_new_print_file_asks_for_its_metadata_once()
    {
        var requested = new List<string>();
        var listener = new MoonrakerListener(Guid.NewGuid(), new RecordingSink(), fileChanged: requested.Add);

        listener.OnStatusQueried(Json("""{"status":{"print_stats":{"filename":"a.gcode","state":"printing"}}}"""));
        listener.OnStatusUpdate(Json("""{"print_stats":{"print_duration":12.0}}"""), 1);
        listener.OnStatusUpdate(Json("""{"print_stats":{"filename":"b.gcode"}}"""), 2);

        Assert.Equal(["a.gcode", "b.gcode"], requested);
    }

    [Fact]
    public void A_cleared_file_name_asks_for_nothing()
    {
        var requested = new List<string>();
        var listener = new MoonrakerListener(Guid.NewGuid(), new RecordingSink(), fileChanged: requested.Add);
        listener.OnStatusQueried(Json("""{"status":{"print_stats":{"filename":"a.gcode"}}}"""));

        listener.OnStatusUpdate(Json("""{"print_stats":{"filename":""}}"""), 1);

        Assert.Equal(["a.gcode"], requested);
    }

    [Fact]
    public void Metadata_of_another_file_is_ignored()
    {
        var (listener, sink) = Create();
        listener.OnStatusQueried(Json("""{"status":{"print_stats":{"filename":"b.gcode","state":"printing"}}}"""));
        var sent = sink.Sent.Count;

        listener.OnFileMetadata("a.gcode", null, new LayerMetadata(LayerCount: 100, LayerHeight: 0.2));

        Assert.Equal(sent, sink.Sent.Count);
        Assert.Null(sink.Sent[^1].PrintStatus.LayersCount);
    }
}
