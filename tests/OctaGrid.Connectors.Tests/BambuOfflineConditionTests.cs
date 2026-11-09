using OctaGrid.Connectors.BambuLab;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Connectors.Tests;

public class BambuOfflineConditionTests
{
    private sealed class SignalSink : ITelemetrySink
    {
        private readonly TaskCompletionSource<PrinterTelemetry> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PrinterTelemetry> First => _first.Task;

        public void Send(PrinterTelemetry telemetry) => _first.TrySetResult(telemetry);
    }

    private static async Task<PrinterTelemetry> FirstReportAsync(Dictionary<string, string> settings)
    {
        var profile = new PrinterProfile(Guid.NewGuid(), "octagrid.bambu", settings);
        await using var client = new MqttClient(profile);
        var sink = new SignalSink();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var run = client.RunAsync(sink, cts.Token);
        var first = await sink.First.WaitAsync(cts.Token);
        await cts.CancelAsync();
        await run;
        return first;
    }

    [Fact]
    public async Task A_missing_access_code_is_reported_as_a_connection_condition()
    {
        var telemetry = await FirstReportAsync(new Dictionary<string, string> { ["url"] = "192.168.1.50" });

        Assert.Equal(UniversalPrinterState.Offline, telemetry.State);
        var condition = Assert.Single(telemetry.Conditions);
        Assert.Equal("connection.failed", condition.Code);
        Assert.Equal(PrinterSeverity.Error, condition.Severity);
        Assert.Contains("access code", condition.Message);
    }

    [Fact]
    public async Task An_address_that_cannot_be_parsed_is_reported_as_a_connection_condition()
    {
        var telemetry = await FirstReportAsync(new Dictionary<string, string> { ["url"] = "", ["apiKey"] = "12345678" });

        var condition = Assert.Single(telemetry.Conditions);
        Assert.Equal("connection.failed", condition.Code);
        Assert.Contains("not a valid printer address", condition.Message);
    }
}
