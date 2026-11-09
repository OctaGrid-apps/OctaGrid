using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OctaGrid.Client.Services;
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
}
