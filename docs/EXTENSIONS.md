# Extensions

OctaGrid loads printer connectors from two places: connectors that ship with the app, and extensions that
users put in the extensions folder. Both appear under **Settings > Extensions**.

## Extensions folder

Extensions are loaded from `extensions` inside the OctaGrid data folder
(`%AppData%\OctaGrid\extensions` on Windows). Two layouts are supported:

```text
extensions/
  acme.prusalink/            installed package: <id>/<version>/
    1.0.0/
      octagrid-extension.json
      Acme.PrusaLink.dll
    1.1.0/                   only the highest version is loaded
      ...
  my-connector/              developer folder: a build output folder with the manifest next to the DLL
    octagrid-extension.json
    MyConnector.dll
```

Extensions are found when OctaGrid starts. Use **Settings > Extensions** to install and remove them (see below); if you copy files into the folder yourself, restart OctaGrid.

## Manifest

Every extension has an `octagrid-extension.json` at its root:

```json
{
  "id": "acme.prusalink",
  "name": "PrusaLink",
  "version": "1.0.0",
  "publisher": "Acme",
  "kind": "connector",
  "host": ">=1.0.19-pre-alpha <2.0.0",
  "assembly": "Acme.PrusaLink.dll",
  "entryType": "Acme.PrusaLink.PrusaLinkConnector"
}
```

| Field | Meaning |
|---|---|
| `id` | `publisher.name`, lowercase letters, digits and hyphens. Must equal the connector's `Descriptor.Id`. |
| `version` | SemVer version of the extension. |
| `host` | Versions of OctaGrid the extension works with: comparators separated by spaces (`>=`, `>`, `<=`, `<`, `=`). |
| `assembly` | The extension DLL, relative to the manifest. It cannot point outside the folder. |
| `entryType` | Full name of the class that implements `IConnector`. |
| `permissions` | Optional list such as `["network", "files"]`. Shown when the user is asked to trust the extension. It is a declaration, not a sandbox. |
| `projectUrl` | Optional web page of the extension (repository or home page), an absolute `http` or `https` address (anything else is ignored and logged as a warning). Shown as a link in the extension list. |

The class needs a public parameterless constructor, or one that takes an `ILoggerFactory`.

### Host version ranges

A range is a list of comparators that must all hold: `>=1.1.0 <2.0.0` means "1.1.0 or later, before 2.0.0".

**Pre-releases.** A pre-release OctaGrid (for example `1.0.20-pre-alpha.1`) is only accepted by a range that names a
pre-release somewhere, such as `>=1.0.19-pre-alpha <2.0.0`. That tells OctaGrid the extension may run on pre-releases, and
the comparators then decide as usual: this range accepts `1.0.19-pre-alpha.7` and `1.0.20-pre-alpha.1`, and still refuses
`2.0.0-pre-alpha.1` (the next major version has started) and `1.0.18-pre-alpha.2`. A range such as `>=1.0.0 <2.0.0` accepts
stable versions of OctaGrid only, so while OctaGrid is in pre-alpha write the lower bound with the pre-release label.
When an extension is refused for this reason, the message says what to write.

## States

| State | Meaning |
|---|---|
| Loaded | Available to printers. |
| Needs approval | Installed, but not run yet. Its code is not loaded until the user trusts it. |
| Disabled | Turned off by the user, or removed and waiting for a restart. |
| Incompatible | The `host` range does not include this version of OctaGrid. The reason names both versions. |
| Failed | The manifest, the assembly or the connector could not be loaded, or the id is already used. The reason says why. |

One extension that is incompatible or fails never stops OctaGrid or the other extensions from loading.

## Isolation

Each extension is loaded into its own `AssemblyLoadContext`. The connector SDK (`OctaGrid.Extensions.Abstractions`),
`OctaGrid.Protocol`, `Microsoft.Extensions.*` and the .NET libraries come from OctaGrid, so `IConnector` is the same
type on both sides. Any other library the extension uses (for example an MQTT client) is loaded from the extension's
own folder, so it cannot clash with the versions OctaGrid or other extensions use.

Extensions run in the OctaGrid process with the same permissions as OctaGrid. Only install extensions you trust.

## Installing, trusting and removing

**Install** from **Settings > Extensions**: *Install from file…* takes a `.octaext` (a zip) or `.zip` file, *Install from
folder…* takes a folder. Both need `octagrid-extension.json` at the top level (a zip may keep everything inside one
top-level folder). The package is copied to `extensions/<id>/<version>/`. Packages are limited to 5000 files and 256 MB
unpacked, and a package with a path that leaves its folder is rejected. Installing never runs any extension code.

**Trust.** An installed extension starts as *Needs approval*. **Review…** shows the publisher and the declared
permissions and asks whether to trust it. Only then is its code loaded, and it works immediately, with no restart.
The approval is saved for that exact version: installing another version asks again. Extensions that are already
approved load at start. Built-in extensions need no approval.

**Remove** deletes an installed extension and forgets the approval. An extension that is running has its files locked on
Windows, so it is turned off at once and its folder is marked and deleted the next time OctaGrid starts. Built-in
extensions cannot be removed, only turned off. To install a newer version of a running extension, install it and
restart OctaGrid.

## Developer mode

Developer mode loads an extension straight from the folder you build it into, so you do not install it, restart
OctaGrid or approve it after every change.

**Turn it on** in one of three ways (the first that is set wins):

1. `OctaGrid --extensions-dev <folder>` (or `--extensions-dev=<folder>`), for a launch profile.
2. The environment variable `OCTAGRID_EXTENSIONS_DEV=<folder>`.
3. **Settings > Extensions > Developer mode > Choose folder…**, which is remembered for the next start.

The folder is either one extension (it contains `octagrid-extension.json`) or a folder with several extension folders in
it. To get the manifest into your build output, add this to the extension project:

```xml
<ItemGroup>
  <None Include="octagrid-extension.json" CopyToOutputDirectory="PreserveNewest" />
  <!-- The host provides the SDK and protocol types; do not ship your own copies. -->
  <ProjectReference Include="..\OctaGrid.Extensions.Abstractions\OctaGrid.Extensions.Abstractions.csproj" Private="false" />
</ItemGroup>
```

What developer mode does:

- **No approval.** You pointed OctaGrid at your own folder, so the extension is trusted. It is listed as *Developer folder*.
- **Reloads on every build.** OctaGrid watches the folder (`.dll` and `.json` files) and, 0.75 seconds after the last
  write, stops the printers that use the extension, unloads the old version, loads the new one and starts the printers
  again. **Reload** in the dialog does the same on demand.
- **Runs from a copy.** The files are copied to a temporary folder first, so your next build can overwrite the originals
  while the old version is still loaded. Leftover copies are cleaned up the next time developer mode starts.
- **Shows build errors.** A manifest that is broken or an extension that cannot be created is listed as *Failed* with the
  reason, and the next good build fixes it.
- **Debugging works.** The extension runs in the OctaGrid process: start OctaGrid with `--extensions-dev <folder>` from
  your IDE, or attach to it, and set breakpoints as usual.

A developer extension cannot have the same id as a built-in or installed extension; remove or rename the other one. It
cannot be removed with **Remove**: choose **Turn off** under *Developer mode* instead.

To try it with the sample, run OctaGrid with `--extensions-dev samples/OctaGrid.Extensions.Sample/bin/Debug/net11.0/package/octagrid-samples.demo-printer`,
change `DemoPrinterConnector.cs` (for example the display name) and run `dotnet build samples/OctaGrid.Extensions.Sample`:
the list in Settings > Extensions shows the new name a moment later.

## Reporting state and problems

A connector reports a printer by calling `ITelemetrySink.Send` with a `PrinterTelemetry` snapshot: temperatures, the
state and the print progress. Send a new snapshot when something changes; do not change one after sending it.

**Problems are conditions, not messages.** `PrinterTelemetry.Conditions` lists everything that is wrong with the printer
*right now*, each as a `PrinterCondition` with a stable `Code` (such as `connection.failed`), a `Severity` and a short
`Message`. Every snapshot carries the complete set, so a problem that is over is simply left out; there is nothing to
clear. Keep the same `Code` while the same problem lasts, and keep the list to a handful of entries. OctaGrid keeps at
most 10 per snapshot (the most serious), cuts long text, and ignores entries without a code or message.

**You do not write a history.** OctaGrid compares each snapshot with the previous one and records an event when the state
changes and when a condition appears, changes or goes away. A client that connects later, or is closed and reopened,
asks for those events (the printer card's *Show events* button). The history is kept in memory, per printer, in a fixed
size (the oldest events are dropped first), and it starts over when OctaGrid restarts. Repeats of the previous event are
not recorded again, and a burst of information events is rate-limited; warnings and errors are never dropped by that limit.

While the state is `Offline`, a condition that is missing from a snapshot counts as *unknown*, not *resolved*: a connector
that cannot reach the printer cannot know that a problem went away. The "resolved" event is recorded once the printer
reports again without it.

## Reporting the console

Raw console output (for example G-code responses such as `ok` or a temperature report) is not state and does not belong in
a snapshot. A connector that can read it implements `IConsoleOutput` and declares `ConnectorCapabilities.Console`. Before
the session runs, OctaGrid calls `SetConsoleSink` with an `IConsoleSink`; write each line the printer reports to its
`WriteLine`, from any thread. Printers of that connector then get a **Console** tab on their page, and printers of other
connectors do not.

You can pass on whatever the printer sends. OctaGrid splits text into lines, skips empty ones, replaces control
characters, cuts very long lines and rate limits a printer that floods the console (it then records how many lines were
skipped). It keeps the last 500 lines per printer in memory, and they are gone when OctaGrid restarts.

`IConsoleOutput` is read-only: it does not let anyone send a command to the printer.

### Taking commands

A connector that can also run what the user types (a line of G-code, a Klipper macro) implements `IConsoleInput` and
declares `ConnectorCapabilities.ConsoleCommands`, together with `Console` so the user sees the answer. The host calls
`SendCommandAsync` with one command; return when the printer accepted it, and write what the printer answers to the
console sink. When the printer rejects the command or cannot be reached, throw an exception whose message tells the
user what went wrong (an `AppException` such as `ValidationException` for a rejected command).

Sending a command can change or stop what the printer is doing, so OctaGrid is careful with it:

- The command box appears only when the user turned on **Expert mode** in Settings (it asks first), and only for
  connectors that declare `ConsoleCommands`.
- A command is one line of plain text of at most 256 characters (`ConsoleCommandRules`). OctaGrid checks this where the
  user types it and again before it reaches your connector, so you never get several lines, control characters or
  Unicode line separators.
- Expert mode is the user's confirmation that they know what they send, so commands are not confirmed one by one.
- Commands can overlap. The box is emptied as soon as a command is on its way and OctaGrid does not wait for it to
  finish before passing on the next, so an emergency stop (`M112`) can reach a printer that is still busy with a long
  command. Your connector has to cope with a call while an earlier one is still running.
- The command is shown in the console as `> command` before it is sent. Only its name (the first word) is written to the
  application log, because the arguments can hold a secret such as a Wi-Fi password.

## Not available yet

## Not available yet

- Installing from a catalog, and updates.
- Loading an installed extension without a restart.
- Out-of-process extensions (Microsoft Store packages).

## Try it: the sample extension

`samples/OctaGrid.Extensions.Sample` is a connector for a simulated printer. It needs no hardware, so it is the quickest
way to try installing, trusting and removing an extension. It also shows what a small connector looks like (settings,
print control, files, a custom command and a console that takes commands).

1. Build it:
   ```bash
   dotnet build samples/OctaGrid.Extensions.Sample
   ```
   This writes the manifest and creates the package in `samples/OctaGrid.Extensions.Sample/bin/Debug/net11.0/package/`:
   the folder `octagrid-samples.demo-printer` and the same thing zipped as `OctaGrid.Extensions.Sample.octaext`.
2. In OctaGrid open **Settings > Extensions** and choose **Install from file…** (the `.octaext`) or **Install from
   folder…** (the `octagrid-samples.demo-printer` folder). The extension appears as *Needs approval*.
3. Choose **Review…**, read the prompt and **Trust and turn on**. *Demo printer (simulated)* is now active.
4. Add a printer (Printers > add) and pick *Demo printer (simulated)* as its connector. Set *Simulation speed* to **Fast**
   to see a whole print in seconds. Start a file such as `benchy.gcode`, then pause, resume or stop it. The *Say hello*
   custom command answers with a message. Open the printer's page (the open icon on its card) and choose the **Console**
   tab to see the lines the simulated printer writes. To send it commands, turn on **Expert mode** in Settings
   (Settings > Advanced) and try `M105`, `M115` or `G28`; anything else is rejected, which shows how a refused command
   looks.
5. Choose **Remove** to take it out again. It is turned off at once and its folder is deleted at the next start.

To see the other states, edit the installed copy of `octagrid-extension.json` (in `extensions/octagrid-samples.demo-printer/1.0.0/`)
and restart OctaGrid: set `"host": ">=9.0.0"` for *Incompatible*, or a wrong `"entryType"` for *Failed* after you approve it.
The sample's `host` range starts at the OctaGrid version it was built with, so rebuild it after the version changes.
