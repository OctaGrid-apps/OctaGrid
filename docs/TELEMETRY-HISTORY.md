# Design: telemetry history and printer watching

Status: steps 1 to 3 committed, step 4 built (uncommitted); step 5 not started. Decisions taken: the agent does not save history across restarts; the chart follows Mainsail's model; the history backfill is built together with subscription-based telemetry; pre-alpha, so replaced types and messages are removed rather than kept for compatibility.

## Goal

Every client that opens a printer shows the same recent temperature history, however and whenever it connected. A desktop client that has watched a printer for an hour and a WASM tab opened a minute ago draw the same chart. At the same time, a client receives telemetry only for the printers it shows, which Remote mode needs anyway (finding in [ARCHITECTURE-REVIEW.md](ARCHITECTURE-REVIEW.md)). Local mode keeps working and behaves the same.

## Where we are

- `TemperatureHistory` and `TemperatureHistoryStore` (`OctaGrid.Client/Models`) keep 600 samples at one-second spacing (10 minutes) per printer, in client memory. `MessageHandler` records every `PrinterTelemetry` it receives. Nothing is persisted.
- A sample is stamped with the **client's** clock when the message arrives. In Remote mode this would mix clock skew and network delay into the timeline.
- The history has fixed nozzle and bed fields, like `PrinterTelemetry` itself (the typed-readings finding in ARCHITECTURE-REVIEW). A chamber or a second extruder has no place.
- A snapshot without temperatures (offline, connecting) is skipped; otherwise the client fills a missing value from its last sample.
- Every printer's telemetry goes to every client. A client that starts showing printers calls `PrinterCommandsService.RequestState`, which sends `RequestPrinterStateRequest` so the agent repeats the current state as notifications. That is a subscription without an unsubscribe.
- Console lines and printer events already live on the agent and are paged with a cursor (`GetPrinterConsoleRequest`, `GetPrinterEventsRequest`), checked with `Permission.View`. History follows the same pattern.

## Options considered

| Where history lives | For | Against |
|---|---|---|
| Client (today) | Simple | Differs on every device; lost on reload |
| **Agent (chosen)** | Sees every reading, runs all the time, sits next to the printer. Same path in Local and Remote mode. Matches console and events | Lost when the agent restarts (accepted); nothing while the agent is offline |
| Server | Survives anything; allows long-term history | Telemetry is planned to go agent→client over WebRTC, so the server is not in that path. Needs a second stream and hosted storage. Kept for later (tier 2) |

## Design

### 1. The agent records samples, timed by its clock and keyed by sensor

History uses its own types in `Protocol/Messaging`:

```csharp
public sealed record TelemetrySample(DateTimeOffset Time, IReadOnlyList<TelemetryReading> Readings);
public sealed record TelemetryReading(string Key, double Value, double? Target);   // TelemetryReadingKeys.Nozzle, .Bed
```

Today the agent maps nozzle and bed to two keys. When `PrinterTelemetry` gets typed readings (chamber, a second extruder), only that mapping changes. Heater power is left out until a connector reports it; it would be an extra optional member on `TelemetryReading`.

**The sample's time is the cursor.** It comes from the agent's `TimeProvider`, so all clients see the same timeline. Clients merge and resume by time ("samples after T"). That needs no sequence numbers and no stream ids: after an agent restart, new samples are simply later. A clock that steps backwards can at worst duplicate or skip a few points on a chart, which is acceptable for this data.

### 2. The agent keeps a ring buffer per printer

`PrinterTelemetryLog` in `Agent.Core/Events`, next to `PrinterConsoleLog` and `PrinterEventLog`. Each snapshot is merged into the printer's last known temperatures first, so every sample is complete and clients no longer fill in gaps. A snapshot without temperatures adds nothing. Samples closer together than one second are not stored (the last known values still move on, so the next stored sample is current). A clock that steps backwards stores the next sample instead of waiting for the old time to come round again.

Size: **1,200 samples at one-second spacing (20 minutes)**, Moonraker's default temperature store, which Mainsail draws from. A printer that reports only on change keeps its last value, and the chart draws it flat (as `TemperatureChart` does today). A printer removed by `ApplyPrinterProfilesRequest` loses its buffer; an agent restart loses all of them (accepted).

The agent records every printer it runs, watched or not, so history is there when a client opens a printer later.

**Telemetry reaches the link like events and console lines.** Today `AgentNotificationHub` is the `ITelemetrySink` that `AgentWorker` writes to. Instead, the worker records each snapshot in the log and raises `TelemetryReported`, and `AgentRequestDispatcher` publishes it, as it does for `EventRecorded` and `ConsoleLineRecorded`. `PrinterTelemetryReported` carries the snapshot and the `TelemetrySample` stored from it (null when none was stored), so clients add exactly what the agent stored.

### 3. Clients watch printers; watching returns the backfill

This replaces `RequestState`. A client declares which printers it shows; the backend answers with each printer's current state and history, then streams live telemetry for those printers only.

**Contract** (`Client.Contracts`):

```csharp
public interface IPrinterTelemetryClient
{
    /// Replaces the set of printers this client shows. Returns state and history for printers newly added,
    /// and for printers listed in resume (after a reconnect), history after the given time only.
    Task<IReadOnlyList<PrinterBackfill>> WatchAsync(
        IReadOnlyCollection<Guid> printerIds,
        IReadOnlyDictionary<Guid, DateTimeOffset>? resume,
        CancellationToken cancellationToken);
}

// Protocol
public sealed record PrinterBackfill(Guid PrinterId, PrinterTelemetry Current, IReadOnlyList<TelemetrySample> History);
```

Setting the whole set (rather than subscribe/unsubscribe pairs) makes reconnecting trivial: the client sends the set again. An empty set means watch nothing.

**Application** (`PrinterWatchService`): takes `userId` and a `watcherId` (a client connection; Local mode has one constant id). It checks `Permission.View` for each printer, keeps each watcher's set, and groups printers by `Printer.AgentId`. It sends each agent:

- `GetPrinterBackfillRequest(IReadOnlyList<PrinterBackfillQuery>)` → `PrinterBackfillReply(IReadOnlyList<PrinterBackfill>)`, where `PrinterBackfillQuery(Guid PrinterId, DateTimeOffset? After)`. The agent bounds the result to its buffer.
- `SetWatchedPrintersRequest(IReadOnlyList<Guid> PrinterIds)` → `AgentAck`, with the union of all watchers' printers on that agent, sent only when the union changes. The agent streams telemetry only for those printers. A connection that closes sets an empty list; the Remote-mode hub does that, and sends each agent its list again when it connects.

In Remote mode the server holds the watcher sets. How the agent addresses one WebRTC peer rather than "all watchers" is part of the WebRTC design; this design only needs the agent to know which printers anyone is watching.

Each new message needs a wire name, a sample in `AgentMessageSamples` and an entry in `AgentMessageSetTests`. `PrinterWatchService.Watch` gets a row in `RoleEnforcementTests`.

`RequestPrinterStateRequest`, `PrinterCommandsService.RequestState` and their contract method are **removed**, along with their wire name, since nothing outside this repository speaks the protocol yet. The agent answers the backfill from the last snapshot it keeps, so `IPrinterSession.RequestStateAsync` and `DeviceOrchestrator.TriggerInitialStateAsync` lose their only caller; both are removed. BambuLab sends `pushall` and Moonraker subscribes on every connect, so neither needed the host to ask.

As built in step 2: the wire names are `telemetry.backfill` and `telemetry.watch` (reply `backfill`). `PrinterTelemetryLog.Get` returns the `PrinterBackfill` (last snapshot and samples under one lock), and `AgentRequestDispatcher` holds the watched set and drops telemetry of printers outside it. Events and console lines still go out for every printer.

### 4. The client watches what is on screen

A `PrinterWatcher` service in `Client` counts watches per printer. As built, the holder is the dashboard card: the dashboard keeps a card for every enabled printer for its lifetime (they keep receiving telemetry while another page is open), and the printer page shows the dashboard's card, so it needs no watch of its own. A card watches in its constructor and stops in `Detach`.

Every new watch asks for the printer's state, even when the printer is already watched: a card created for an already watched printer (refresh, connector change) would otherwise show "Connecting" until the printer's next report. It does so through `resume`, with the time of the last sample the client has (`DateTimeOffset.MinValue` for all), so only the missing history comes back. One call runs at a time and always sends the latest set; changes made meanwhile go out with the next call. A failed call is logged, and the next change asks again for the state that did not arrive.

Merging without gaps or duplicates: live samples that arrive before the backfill are kept, and `TemperatureHistory.Merge` combines them with the history by time, each time once. A live sample not later than the newest kept one is dropped. The backfill's current state goes through `IClientMessageHandler` like live telemetry.

`IClientMessageHandler.Handle` takes the telemetry and the `TelemetrySample` the agent stored, and `MessageHandler` adds only that sample to the store. `TemperatureHistoryStore` stays as the client cache, so moving between pages does not fetch again. `TemperatureHistory` keeps 1,200 samples by agent time and has no clock, interval check or gap filling of its own; it still converts samples to nozzle/bed values for the chart until step 5.

### 5. Chart, following Mainsail

The current `TemperatureChart` already has solid lines, dashed targets, a time axis that grows to the window and a flat line for idle printers. To match Mainsail:

- **Series per reading key**, not fixed hotend and bed properties, with colours from theme resources per key (a palette for keys without their own colour).
- **Legend with toggles**: each series can be hidden; the choice is remembered per client.
- **Hover read-out**: a vertical line at the pointer with time, value and target of every visible series.
- **20-minute window**, from the buffer size above.

The chart stays a custom `Control` drawing from the store. These items are independent of the protocol work.

### Tier 2, later: long-term history on the server

Not part of this change; recorded so this design leaves room for it. The agent sends a thinned stream to the server over SignalR (for example one sample every 10 to 30 seconds, or only when a value moves past a threshold) using `TelemetrySample`. The server stores it per print job and printer, as a candidate for the hosted plan, with retention that self-hosted builds can turn off. Recent history still comes from the agent.

## Steps

1. `TelemetrySample` / `TelemetryReading`; `PrinterTelemetryLog` with merged complete samples; telemetry reaches the hub through `AgentWorker.TelemetryReported` and the dispatcher; `PrinterTelemetryReported` carries the stored sample. Agent and protocol tests.
2. `GetPrinterBackfillRequest`, `SetWatchedPrintersRequest` and their wire-contract tests; the agent filters streamed telemetry by the watched set.
3. `PrinterWatchService`, `IPrinterTelemetryClient`, the Local adapter (one watcher); `RoleEnforcementTests`. `RequestState`, `RequestPrinterStateRequest`, `IPrinterSession.RequestStateAsync` and `ICommandService` removed. The design-time fake of `IPrinterTelemetryClient` comes with step 4, when a view model first uses it.
4. `PrinterWatcher` in the client; dashboard cards watch their printer until detached; the store holds agent samples and merges backfill and live samples; `DesignPrinterTelemetryClient` for design time.
5. Chart: series per key, legend toggles, hover read-out.

Steps 1 to 4 belong to one change. Step 5 can follow on its own.

## Feature improvements (later)

- **Seed from Moonraker's temperature store.** When the Moonraker connector connects, it could fill the agent's buffer from Moonraker's own store (`server.temperature_store`), so Klipper printers keep their history across an agent restart. An optional connector capability.
- **Heater power** as a faint area on a 0–100 % axis, once a connector reports it (Moonraker does per heater).
