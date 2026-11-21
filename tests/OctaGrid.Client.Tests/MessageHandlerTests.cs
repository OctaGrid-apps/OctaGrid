using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OctaGrid.Client.Services;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Tests;

[Collection(UiCollection.Name)]
public class MessageHandlerTests
{
    private static PrinterEventRecorded Recorded(Guid printerId) =>
        new(printerId, Guid.NewGuid(), new PrinterEvent(1, DateTimeOffset.UnixEpoch, PrinterSeverity.Info, "test", "something"));

    [Fact]
    public async Task A_printer_event_reaches_only_that_printers_listeners_and_only_on_the_ui_thread()
    {
        var printerId = Guid.NewGuid();
        var received = new List<PrinterEventData>();
        var elsewhere = new List<PrinterEventData>();
        var listener = new object();
        var bystander = new object();
        WeakReferenceMessenger.Default.Register<PrinterEventData, Guid>(listener, printerId, (_, m) => received.Add(m));
        WeakReferenceMessenger.Default.Register<PrinterEventData, Guid>(bystander, Guid.NewGuid(), (_, m) => elsewhere.Add(m));

        List<int> counts = [];
        await HeadlessUi.Run(() =>
        {
            new MessageHandler().Handle(Recorded(printerId));
            counts.Add(received.Count);   // posted, not delivered inline: the caller may be any thread
            Dispatcher.UIThread.RunJobs();
            counts.Add(received.Count);
        });

        Assert.Equal([0, 1], counts);
        Assert.Equal(printerId, received[0].Value.PrinterId);
        Assert.Empty(elsewhere);
        GC.KeepAlive(listener);
        GC.KeepAlive(bystander);
    }

    [Fact]
    public async Task Telemetry_reported_on_another_thread_reaches_its_printer_on_the_ui_thread()
    {
        var printerId = Guid.NewGuid();
        var onUiThread = new List<bool>();
        var listener = new object();
        WeakReferenceMessenger.Default.Register<PrinterTelemetryData, Guid>(listener, printerId,
            (_, _) => onUiThread.Add(Dispatcher.UIThread.CheckAccess()));

        // Simulates telemetry reported on an agent thread.
        await Task.Run(() => new MessageHandler().Handle(new PrinterTelemetry(printerId), null), TestContext.Current.CancellationToken);
        await HeadlessUi.Run(() => Dispatcher.UIThread.RunJobs());

        Assert.Equal([true], onUiThread);
        GC.KeepAlive(listener);
    }

    [Fact]
    public async Task A_printer_change_on_another_thread_reaches_the_printer_and_all_listeners_on_the_ui_thread()
    {
        var printerId = Guid.NewGuid();
        var forPrinter = new List<bool>();
        var forAll = new List<bool>();
        var printerListener = new object();
        var allListener = new object();
        WeakReferenceMessenger.Default.Register<PrinterEntityUpsert, Guid>(printerListener, printerId,
            (_, _) => forPrinter.Add(Dispatcher.UIThread.CheckAccess()));
        WeakReferenceMessenger.Default.Register<PrinterEntityUpsert>(allListener,
            (_, _) => forAll.Add(Dispatcher.UIThread.CheckAccess()));

        var change = new EntityChanged(EntityTypes.Printer, printerId, 1, ChangeKind.Upserted, Guid.NewGuid());
        await Task.Run(() => new MessageHandler().Handle(change), TestContext.Current.CancellationToken);
        await HeadlessUi.Run(() => Dispatcher.UIThread.RunJobs());

        Assert.Equal([true], forPrinter);
        Assert.Equal([true], forAll);
        GC.KeepAlive(printerListener);
        GC.KeepAlive(allListener);
    }
}
