using OctaGrid.Application.Services;
using OctaGrid.Application.Telemetry;
using OctaGrid.Data;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.AgentLink.Requests;

namespace OctaGrid.Application.Tests;

public sealed class PrinterWatchServiceTests : IDisposable
{
    private static readonly Guid Desktop = Guid.NewGuid();
    private static readonly Guid Browser = Guid.NewGuid();

    private readonly AgentLinkFixture _fixture = new();
    private readonly PrinterWatchRegistry _registry = new();

    public void Dispose() => _fixture.Dispose();

    private async Task<IReadOnlyList<Protocol.Messaging.PrinterBackfill>> Watch(
        Guid watcherId, Guid[] printerIds, IReadOnlyDictionary<Guid, DateTimeOffset>? resume = null, Guid? userId = null)
    {
        await using var context = _fixture.NewContext();
        return await NewService(context).Watch(userId ?? _fixture.UserId, watcherId, printerIds, resume, CancellationToken.None);
    }

    private PrinterWatchService NewService(OctaGridContext context) => new(_fixture.Printers(context), _fixture.Link, _registry);

    private List<SetWatchedPrintersRequest> WatchRequests() =>
        _fixture.Link.Sent.Select(s => s.Request).OfType<SetWatchedPrintersRequest>().ToList();

    private List<GetPrinterBackfillRequest> BackfillRequests() =>
        _fixture.Link.Sent.Select(s => s.Request).OfType<GetPrinterBackfillRequest>().ToList();

    [Fact]
    public async Task Watching_tells_the_agent_first_and_returns_state_and_history_of_every_printer()
    {
        var first = await _fixture.CreatePrinter();
        var second = await _fixture.CreatePrinter();

        var backfills = await Watch(Desktop, [first.Id, second.Id]);

        Assert.Equal([first.Id, second.Id], backfills.Select(b => b.PrinterId));
        Assert.All(backfills, b => Assert.Equal(UniversalPrinterState.Idle, b.Current.State));
        Assert.All(_fixture.Link.Sent, s => Assert.Equal(_fixture.AgentId, s.AgentId));
        Assert.IsType<SetWatchedPrintersRequest>(_fixture.Link.Sent[0].Request);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), Assert.Single(WatchRequests()).PrinterIds.Order());
        Assert.All(Assert.Single(BackfillRequests()).Printers, q => Assert.Null(q.After));
    }

    [Fact]
    public async Task Watching_the_same_printers_again_sends_nothing_and_returns_nothing()
    {
        var printer = await _fixture.CreatePrinter();
        await Watch(Desktop, [printer.Id]);
        _fixture.Link.Sent.Clear();

        var backfills = await Watch(Desktop, [printer.Id]);

        Assert.Empty(backfills);
        Assert.Empty(_fixture.Link.Sent);
    }

    [Fact]
    public async Task Adding_a_printer_returns_only_the_new_one_and_sends_the_whole_set()
    {
        var first = await _fixture.CreatePrinter();
        var second = await _fixture.CreatePrinter();
        await Watch(Desktop, [first.Id]);
        _fixture.Link.Sent.Clear();

        var backfills = await Watch(Desktop, [first.Id, second.Id]);

        Assert.Equal([second.Id], backfills.Select(b => b.PrinterId));
        Assert.Equal(new[] { first.Id, second.Id }.Order(), Assert.Single(WatchRequests()).PrinterIds.Order());
    }

    [Fact]
    public async Task A_resumed_printer_is_returned_again_from_its_last_sample()
    {
        var printer = await _fixture.CreatePrinter();
        await Watch(Desktop, [printer.Id]);
        _fixture.Link.Sent.Clear();
        var last = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

        var backfills = await Watch(Desktop, [printer.Id], new Dictionary<Guid, DateTimeOffset> { [printer.Id] = last });

        Assert.Equal([printer.Id], backfills.Select(b => b.PrinterId));
        Assert.Equal(new PrinterBackfillQuery(printer.Id, last), Assert.Single(Assert.Single(BackfillRequests()).Printers));
        Assert.Empty(WatchRequests());
    }

    [Fact]
    public async Task An_agent_streams_a_printer_while_any_watcher_shows_it()
    {
        var printer = await _fixture.CreatePrinter();
        await Watch(Desktop, [printer.Id]);
        await Watch(Browser, [printer.Id]);

        await Watch(Desktop, []);
        Assert.Single(WatchRequests());

        await Watch(Browser, []);
        Assert.Empty(WatchRequests()[^1].PrinterIds);
    }

    [Fact]
    public async Task A_printer_without_an_agent_is_offline_and_no_agent_is_asked_about_it()
    {
        var printer = await _fixture.CreatePrinter();
        _fixture.UnassignPrinter(printer.Id);

        var backfill = Assert.Single(await Watch(Desktop, [printer.Id]));

        Assert.Equal(UniversalPrinterState.Offline, backfill.Current.State);
        Assert.Empty(backfill.History);
        Assert.Empty(_fixture.Link.Sent);
    }

    [Fact]
    public async Task The_printers_of_an_unreachable_agent_are_offline_instead_of_failing_the_call()
    {
        var printer = await _fixture.CreatePrinter();
        _fixture.Link.Unreachable.Add(_fixture.AgentId);

        var backfill = Assert.Single(await Watch(Desktop, [printer.Id]));

        Assert.Equal(UniversalPrinterState.Offline, backfill.Current.State);
    }

    [Fact]
    public async Task A_printer_the_user_may_not_view_comes_back_offline_and_is_not_streamed()
    {
        var printer = await _fixture.CreatePrinter();

        var backfill = Assert.Single(await Watch(Desktop, [printer.Id], userId: Guid.NewGuid()));

        Assert.Equal(UniversalPrinterState.Offline, backfill.Current.State);
        Assert.Empty(_fixture.Link.Sent);
    }

    [Fact]
    public async Task An_agent_that_refused_its_watch_list_is_sent_it_again_on_the_next_call()
    {
        var printer = await _fixture.CreatePrinter();
        _fixture.Link.Refused = typeof(SetWatchedPrintersRequest);
        await Assert.ThrowsAnyAsync<AppException>(() => Watch(Desktop, [printer.Id]));

        _fixture.Link.Refused = null;
        await Watch(Desktop, [printer.Id]);

        Assert.Equal([printer.Id], Assert.Single(WatchRequests()).PrinterIds);
    }

    [Fact]
    public async Task An_agent_that_was_not_connected_is_sent_its_watch_list_on_the_next_call()
    {
        var printer = await _fixture.CreatePrinter();
        _fixture.Link.Unreachable.Add(_fixture.AgentId);
        await Watch(Desktop, [printer.Id]);

        _fixture.Link.Unreachable.Clear();
        await Watch(Desktop, [printer.Id]);

        Assert.Equal([printer.Id], Assert.Single(WatchRequests()).PrinterIds);
    }
}
