# ADR 0001: Connector extension API

- **Status:** Proposed
- **Date:** 2026-11-05 (updated 2026-11-06)
- **Scope:** Agent-side printer connectors (Bambu Lab MQTT, Moonraker, third-party)

## Context

Printer support is currently compiled into `OctaGrid.Agent.Core`. We want third-party developers
to add connectors, with some shipped and enabled by default and others installed by the user
(in the Store build, as separate Store packages; see "Store distribution and prototype findings").
Remote mode, where the Agent runs as a separate
process on Windows, Linux or Docker, is planned next, so the design must not assume the Agent
runs inside the desktop app.

### What exists today

| Seam | Where | Assessment |
|---|---|---|
| `IDeviceClientFactory` (`CanHandle(string protocol)` + `Create(DeviceProfile)`) | `Agent.Core` | Good extension point; protocol is already a string at runtime. |
| `IDeviceClient` (files, print control, telemetry loop, connection test) | `Agent.Core` | Fat: every connector must implement every operation. |
| `IThumbnailProvider` (optional capability, checked with `is`) | `Agent.Core` | The right pattern for optional features; generalise it. |
| `DeviceProfile(DeviceId, Protocol, ConnectionString, Settings)` | `Protocol` | Free-form settings, so the UI cannot render a form for a new connector. |
| `PrinterProtocol` enum (`Moonraker`, `BambuMqtt`, `PrusaLink`) | `Data.Abstractions`, stored on `Printer` | **Blocker:** a closed enum cannot name third-party protocols. |
| `PrinterTelemetry` (fixed nozzle/bed fields) | `Protocol` | Cannot express extra sensors, multiple extruders or chambers. |
| Connectors, MQTTnet, FluentFTP, SharpCompress | `Agent.Core` | Connectors and the host share one assembly and its dependencies. |

## Decision

### 1. Split the assemblies

```
OctaGrid.Extensions.Abstractions   <- the SDK (NuGet package). Contracts only, minimal dependencies.
OctaGrid.Protocol                  <- shared DTOs, referenced by the SDK
OctaGrid.Agent.Core                <- host: orchestrator, extension manager, telemetry pipeline
OctaGrid.Connectors.BambuLab       <- bundled extension (moved out of Agent.Core)
OctaGrid.Connectors.Moonraker      <- bundled extension (moved out of Agent.Core)
```

Bundled connectors are built exactly like third-party ones, so the SDK is exercised by our own code.

### 2. Identify connectors by a stable string id

Replace `PrinterProtocol` with a connector id string (for example `octagrid.bambu-mqtt`,
`octagrid.moonraker`, `acme.prusalink`). It is stored on `Printer`. A migration maps the existing
enum values to the bundled ids. Ids are lowercase `publisher.name` and never change once published.

### 3. Connector contract: a small core plus optional capabilities

```csharp
namespace OctaGrid.Extensions.Abstractions;

public interface IConnector                      // one per extension, found by the host
{
    ConnectorDescriptor Descriptor { get; }
    IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context);
}

public interface IPrinterSession : IAsyncDisposable   // replaces IDeviceClient
{
    Task RunAsync(ITelemetrySink telemetry, CancellationToken ct);     // connect, stream state
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);
    Task RequestStateAsync(CancellationToken ct);                      // was TriggerInitialStateAsync
}

// Optional capabilities. The session implements only what the printer supports.
public interface IPrintControl   { /* Start / Pause / Resume / Stop */ }
public interface IFileStorage    { /* List / Upload / Delete */ }
public interface IThumbnails     { /* GetThumbnailAsync(...), was IThumbnailProvider */ }
public interface ICustomCommands { /* Descriptors, ExecuteAsync(...): Expert mode */ }

public interface IPrinterDiscovery               // implemented by the connector, not the session
{
    IAsyncEnumerable<DiscoveredPrinter> ScanAsync(DiscoveryOptions options, CancellationToken ct);
}
```

`ConnectorDescriptor` is plain data so the Client can use it without loading any code:

- `Id`, `DisplayName`, `Version`, `Publisher`, `IconUri`
- `Capabilities`: flags such as `PrintControl`, `Files`, `Thumbnails`, `CustomCommands`, `Discovery`.
  The host checks that these match the interfaces the session implements.
- `Settings`: a list of fields (`key`, `label`, `type`, `required`, `secret`, `default`, `help`) that
  the Client renders as the "add printer" form. This replaces the free-form `Settings` dictionary and
  the single `ConnectionString`.
- `CustomCommands`: command descriptors (name, parameters, risk level). The Client shows them on the
  printer page and on dashboard cards.

The Client never talks to an extension. It talks to the Agent through `Client.Contracts`, which
means the same UI works for Local and Remote mode, and extensions run where the printer is reachable.

### 4. Telemetry stays typed, with room to grow

`PrinterTelemetry` keeps the standard fields and gains `Extruders` (a list), `Chamber` and
`Sensors` (a list of `id`, `label`, `value`, `unit`). Connectors map vendor data onto the
standard fields first, and use `Sensors` only for what does not fit. This keeps dashboards generic
without forcing every connector to know about the UI.

### 5. Versioning and compatibility

- There is **one version line**. The SDK package `OctaGrid.Extensions.Abstractions` is released together
  with the app and has the same version: the tag `v1.2.0` publishes the app and SDK `1.2.0`, and a
  pre-release such as `1.0.17-pre-alpha` publishes a pre-release SDK. There is no separate "API
  version". The app depends directly on its extensions, so their compatibility is simply the app
  version they were built and tested against.
- Compatibility promise (see `docs/VERSIONING.md`): within a stable MAJOR the SDK only adds members, so
  an extension built for `1.2` works on `1.2` and later `1.x`. A breaking change needs a new MAJOR.
  While the app is in pre-alpha there is no promise: any release may break the SDK, and extensions
  should declare a tight range.
- Each extension ships a manifest `octagrid-extension.json` with a host version range (like VS Code
  `engines` or Obsidian `minAppVersion`):

  ```json
  {
    "id": "acme.prusalink",
    "name": "PrusaLink",
    "version": "1.2.0",
    "publisher": "Acme",
    "host": ">=1.1.0 <2.0.0",
    "assembly": "Acme.PrusaLink.dll",
    "entryType": "Acme.PrusaLink.PrusaLinkConnector"
  }
  ```

- The host loads an extension only if the host's own version satisfies the manifest `host` range. A
  pre-release host (for example `1.0.17-pre-alpha`) satisfies a range only if the range names that
  pre-release line. Bundled connectors always match, because they are built with the app.
- Every extension has one of four states, shown in the Client: **Loaded**, **Disabled** (by the user),
  **Incompatible** (with the reason, for example "requires OctaGrid 2.0 or later, this agent is 1.4") or
  **Failed** (load or run error). An incompatible or failing extension never stops the Agent or other
  extensions. Its printers show "connector unavailable" rather than an error loop.
- Adding an optional capability interface is a MINOR change.

### 6. Loading and isolation

- Each extension loads into its own `AssemblyLoadContext`. The SDK, `OctaGrid.Protocol` and
  `Microsoft.Extensions.*` abstractions resolve from the host's context so type identity matches.
  Everything else (MQTTnet, FluentFTP, a vendor SDK) comes from the extension's folder.
- **Sources** (an `IExtensionSource` abstraction, since where files live differs per platform):
  1. *Bundled*: shipped inside the app package (section 10), loaded in-process, enabled by default.
  2. *User folder*: `<data>/extensions/<id>/<version>/`, installed from the catalog, a package file
     or a developer folder (section 12). Loaded in-process.
  3. *Store extension package*: a separate MSIX that declares an app extension for OctaGrid (see
     "Store distribution and prototype findings"). The Store handles download, signing and update.
     The host discovers it and runs it **out of process** through the package's execution alias.
     Files are never copied.
  4. *Docker / Linux / macOS*: a mounted or installed folder; same as 2.
- User-installed extensions are not enabled until the user confirms a trust prompt that names the
  publisher and the permissions it needs (network, file access). Bundled extensions skip the prompt.

### 7. Remote mode and hosting

Extensions run in the **Agent**, never in the hosted server. The hosted service therefore does not
execute third-party code, which keeps it cheap and safe. For Remote mode:

- Installing, updating, enabling or disabling an extension is an Agent command issued from the Client
  ("Install connector"), subject to the same compatibility checks as the agent update button.
- The compatibility handshake on connect includes the agent version and the
  list of loaded extensions with their states, so the Client can say "update the agent" or "update the
  connector" instead of failing.
- Descriptors (settings schema, capabilities, custom commands) travel as data in the Agent protocol.
  Extensions never ship client-side UI code in v1.
- **WASM and other thin clients:** connector code never runs in the Client, so it never runs in the
  browser. Raw TCP (MQTT, FTPS), process start and file access are not available in WASM anyway. A WASM
  client shows extension-defined content (connector list, settings forms, capabilities, custom
  commands) from descriptors received from the Agent. For that, the descriptor, settings-schema and
  manifest-summary types live in `OctaGrid.Protocol` (WASM-safe, no agent dependencies), while the
  runtime interfaces (`IConnector`, `IPrinterSession`, ...) live in `OctaGrid.Extensions.Abstractions`,
  which only the Agent references. Client-side extension kinds (themes, widgets) are out of scope for
  v1; in WASM they would need pure managed assemblies fetched from the server.

### 8. Security model

In-process .NET plugins run with the full privileges of the Agent. They cannot be sandboxed. For v1 we
accept that and reduce the risk with explicit user trust, Store-signed extension packages, and publisher info.
A future option is out-of-process connectors over JSON-RPC (the repository already uses
`StreamJsonRpc`), which would isolate crashes and allow limited permissions and other languages. The
`IConnector` and `IPrinterSession` contracts are designed so that an out-of-process proxy can implement
them later without changing connectors written in-process.

Custom commands (Expert mode) are declared by the connector, carry a risk level, and require an
explicit user action and confirmation. The Agent never runs a command that the connector did not declare.

### 9. Naming

- **Connector** is the extension kind that connects OctaGrid to a printer's firmware or API
  (`octagrid.moonraker`). It is the name used in code, UI and docs.
- **Extension** is the umbrella term for any installable package. The manifest has `kind: "connector"`,
  so later kinds (themes, dashboard widgets, command packs) need no renaming.
- Rejected: *agent client* (clashes with the Agent component and with the Desktop Client) and *driver*
  (suggests Windows printer drivers or kernel code).
- Names: SDK `OctaGrid.Extensions.Abstractions`; connector projects `OctaGrid.Connectors.<Name>`;
  extension ids `publisher.name`.

### 10. Bundled connectors

OctaGrid cannot work without printer connectors, so the Bambu Lab and Moonraker connectors are **part of
the app**, not optional downloads. They live in this repository and solution, are built and released
with the app, and share the app's version. They are separate assemblies only so that they use the same
SDK as third-party connectors. Only third-party connectors are separate packages. They ship with every
build and are the reference implementations:

- **Repository:** they stay in this repository (`src/Connectors/OctaGrid.Connectors.<Name>`), not in
  separate repositories. This keeps SDK and connector changes atomic, avoids version drift and a second
  release process, and matches common practice (VS Code built-in extensions, Home Assistant core
  integrations and Obsidian core plugins live in the main repository, community ones do not). To keep
  them honest as reference implementations, a build check allows them to reference only
  `OctaGrid.Extensions.Abstractions` and `OctaGrid.Protocol`, never `Agent.Core` internals. Optional
  official connectors that are not bundled may live in their own repositories and are published like
  third-party ones. A `samples/` folder and a template repository serve external developers.
- **Layout:** `extensions/<id>/` next to the app, each with a manifest, exactly like a third-party
  extension. The host sees them as the source *Bundled*.
- **Every distribution:** Velopack installer, portable zip, MSIX (Store) and Docker image contain them,
  because they are part of the build output. Bundled code is fully contained in our package, which is
  what Store policy 10.2.4 asks for.
- **Behaviour:** enabled by default, no trust prompt, can be disabled but not uninstalled, updated with
  the app (through the Store for the Store build).
- **Overrides:** if a user installs a newer version of the same connector id, the higher SemVer wins and
  the other copy is listed as *shadowed*. This allows a connector hotfix without an app release.
- **Execution:** in-process in every build. The main package may execute its own files, so the
  `WindowsApps` restriction and Smart App Control do not apply.
- **Build check:** a test copies the bundled connectors into an `extensions/` folder and loads them
  through the real extension manager, so manifest and API mismatches fail the build.

### 11. Distribution: one format and catalog, several install sources

Store Add-ons are not used to deliver extensions: durable add-ons are licences, and optional packages
are limited to the main app's publisher. They stay available for selling a licence to our own paid
features later. Edge-style extension marketplaces work because the host owns the marketplace and the
installer; for OctaGrid that is our own catalog.

- **Format:** an extension is a folder or `.octaext` zip with `octagrid-extension.json`, built by an
  `octagrid-ext pack` command that also writes file hashes (and an optional signature).
- **Publishing:** authors publish from their own GitHub repository using a provided GitHub Action
  that packs the extension and creates a release. No account with OctaGrid is needed.
- **Index:** a public repository (`octagrid-extensions`) holds one JSON entry per extension (id,
  repository, install source, version). Authors list an extension with a pull request. CI validates the
  manifest schema, API version, hashes and that the id is unclaimed; a short human review follows.
- **In-app catalog:** reads the index and installs by URL with a pinned hash. Trust levels: *Verified*
  (reviewed), *Community* (shown with a warning), *Local* (developer folder or file).
- **Install sources per build:**

  | Build | How an extension is installed |
  |---|---|
  | Microsoft Store | As its own Store (or WinGet) app, listed in the catalog. OctaGrid discovers it and runs it out of process. The Store build does **not** download extension code itself: Store policy 10.2.2 forbids extending an app through dynamic inclusion of code, and 10.2.3 restricts installing secondary software. To be confirmed during certification. |
  | Velopack, portable, Docker, Linux, macOS | The catalog downloads the package into `<data>/extensions/`. |

  Both use the same manifest, compatibility checks and catalog entries; only the transport differs.

### 12. Developer loop without publishing

- `dotnet new octagrid-connector` scaffolds a project, manifest and launch profile.
- `OctaGrid.Extensions.Testing`: a fake printer server and contract tests, so `dotnet test` verifies a
  connector without running OctaGrid.
- **Developer mode** (a setting or `--extensions-dev <folder>`): loads an extension straight from a
  build output folder, with a file watcher and a *Reload extensions* command. Folder extensions run
  in-process, so debugging with F5 works.
- **Install from file:** a `.octaext` can be shared as a beta and installed without the catalog.
- **Store build:** it can load from a folder, but Smart App Control may block unsigned DLLs. Developers
  should use the portable build (SAC is normally off on developer machines) or sign their DLLs.

## Store distribution and prototype findings

Prototype: `prototypes/store-extensions/` (a packaged host plus a sample extension package; see its
README). Run on Windows 11, signed MSIX installed into `C:\Program Files\WindowsApps`, 2026-11-05.

### Which Store mechanism fits

| Mechanism | Third-party developers? | Runs as | Verdict |
|---|---|---|---|
| **App extension** (`uap3:AppExtension`, discovered with `AppExtensionCatalog`) | Yes. Open ecosystem; any developer can ship one as an independent Store app. | Separate package; the host decides how to use its content. | **Use this** for third-party connectors. |
| **Optional package / related set** | No. Closed ecosystem: only the main app's publisher. Code runs with the main app's identity. | Inside the main app's container. | Possible for our own paid first-party connectors only; **Store submission needs permission from Microsoft** (Windows developer support), and executable code must be in a related set. |
| **Store durable add-on** (in-app purchase) | n/a | A licence, not code distribution. | Use only for selling a licence to a connector, not for delivering it. |

So "Store add-ons" for third parties means: each connector is its own Store listing (or WinGet entry,
or a GitHub release) that declares the OctaGrid extension point. OctaGrid can send users to it and
discover it once installed. OctaGrid's own bundled connectors stay in the main package.

### Results

| Question | Result |
|---|---|
| Can the host find third-party extension packages and read their metadata? | **Yes.** `AppExtensionCatalog.Open("octagrid.connector")` returned the extension, its package identity, publisher, signature kind and the custom properties (assembly, entry type, API version). |
| Can the host load the extension DLL in place from the package folder? | **No.** `Access is denied` when loading from the install location and from the `PublicFolder`. `File.Exists` is true, but Windows blocks loading an image from another package's folder. |
| Can the host read the files? | **Yes.** `ReadAllBytes` works. |
| Can the host copy the files into its own data folder and load the copy? | **Yes.** Loaded in a separate `AssemblyLoadContext`, with the SDK assembly resolved from the host's default context, so `IConnector` has the same type on both sides. |
| Can the host start the extension's own executable (out of process) through an execution alias, without copying anything? | **Yes.** The extension declares `windows.appExecutionAlias`; the host started it with `--serve` and exchanged a message over stdin/stdout. The process ran from the extension's own package folder with its own identity. |

### Why in-place loading is denied (and why Store installs won't differ)

The access-denied is **not** caused by sideloading. In every package folder under `WindowsApps`,
`BUILTIN\Users` gets read-only access, and only the package's own identity (its package SID) gets
read **and execute**. We compared the ACLs of our developer-signed package with a Store-installed
package (Windows Calculator): identical structure. Loading `CalculatorApp.dll` from an ordinary
process fails with the same Win32 error 5 (access denied) as loading our sample DLL. Windows lets a
package execute only its own files, so no host can load another package's DLL in place, whoever signed it.

Consequence for the design: the extension manager keeps a versioned cache
`<host LocalState>/extensions/<package family>/<version>/`, filled from the package folder when the
extension is first seen or its version changes, and removed when the package is uninstalled
(`AppExtensionCatalog` raises install, update and uninstall events). The extension's `host` range
from its manifest properties is checked before anything is copied or loaded.

### Smart App Control

Smart App Control (SAC) was **off** on the test machine (`VerifiedAndReputablePolicyState = 0`), and
it cannot be switched back on without reinstalling Windows, so we could not observe enforcement. What
Microsoft documents: in enforcement mode SAC allows a binary only if it is signed with a certificate
from a CA in the Microsoft Trusted Root Program, or the cloud reputation service predicts it is safe;
unsigned, unknown code is blocked. Microsoft tells developers to sign **all** binaries, including DLLs.
Files inside a Store-signed package are covered by the package signature; a **copy** of the DLL in a
user folder loses that, so it would need its own Authenticode signature from a trusted CA (for example
Azure Trusted Signing) or it is likely to be blocked. The documented way to test this without enabling
SAC is the *SmartAppControlAuditNoISG* audit policy (events 3076 in
`Applications and Services Logs > Microsoft > Windows > CodeIntegrity > Operational`); it needs an
administrator and changes the system's code-integrity policy, so it was not applied here.

Conclusion: the copy-and-load path is the one that carries the SAC risk. The out-of-process path does
not, because the extension runs from its own Store-signed package.

### Loading strategy per source

| Source | Platform | How connectors run |
|---|---|---|
| Bundled in OctaGrid | all | In-process (our own signed code). |
| Folder / NuGet / Docker volume | Windows, Linux, macOS, Docker | In-process by default. No package ACLs; on Windows the DLLs should be signed. |
| Store extension package | Windows (Store build) | **Out of process** through the extension's execution alias (recommended). In-process by copying is possible but needs signed DLLs because of SAC. |

This refines decision 1 of the earlier discussion (in-process in v1): in-process stays for the portable
sources, but Store extensions should be out-of-process. It also makes the `IConnector` proxy-friendly
design rule load-bearing, because the Store path needs a proxy over stdio or a named pipe (the repository
already uses `StreamJsonRpc`).

### Not yet verified

- The prototype was signed with the developer certificate (`Signature kind: Developer`), not by the
  Store. The ACL comparison above makes a different result unlikely, but a Store-signed prototype
  would confirm it.
- Whether Store certification accepts an extension-only package that needs a stub executable
  (the manifest requires an `Application` entry) and the `runFullTrust` capability.
- x64 and arm64 behaviour, and bundles.

### What to borrow from PowerToys Command Palette

Command Palette uses the same discovery mechanism (an app extension contract) but runs each extension
**out of process** as a COM server in its own Store app. Ideas worth taking:

- **Separate packages per extension, with a one-package limit:** a host and an extension cannot share
  one MSIX (reported by developers), which matches our "separate listing" conclusion.
- **Several distribution channels, one discovery path:** Microsoft Store and WinGet (a tag such as
  `windows-commandpalette-extension` makes WinGet entries discoverable), plus a curated **Extension
  Gallery** that only links to the install source. We can do the same: a small curated list in the
  Client that links to Store, WinGet or GitHub.
- **A toolkit on top of the raw contract:** raw interfaces plus helper base classes
  (`CommandProvider`, `ListPage`, settings forms, `ISettingsForm`). Our SDK should ship helper base
  classes for the common connector shape, such as polling and reconnect.
- **Declarative UI from the extension:** pages, forms, details, status messages and progress are data,
  not UI code. Matches our settings-schema and custom-command descriptors.
- **A "reload extensions" command** for developers, and host-provided logging and status
  (`IExtensionHost`). We should give connectors an `IConnectorContext` with logging and status
  reporting, plus a developer reload.
- **Out-of-process by default:** this is what made their model language-neutral and crash-proof. It
  fits our parked option (ADR section 8): the same app extension declaration can point to a COM server
  or an app service instead of a DLL.

## Consequences

- Third parties can add printers without touching OctaGrid; bundled connectors become the reference.
- A one-off refactor: move connectors out of `Agent.Core`, replace the `PrinterProtocol` enum and its
  migration, change `DeviceProfile`/`CreatePrinterRequest`, and move `IDeviceClient` to the SDK.
- The SDK becomes a public API with compatibility promises. It is versioned with the app (one version
  line), published from the same tag, and the promise is documented in `docs/VERSIONING.md`.
- In-process extensions are trusted code. The trust prompt and publisher info are part of the product,
  not an afterthought.
- Two install paths (catalog download, and Store packages) must be kept consistent through one manifest
  and one compatibility check.
- We take on running an index repository and its CI, and a small review process for listings.

## Migration plan

Design rule that follows from the in-process decision: SDK types must be serializable data or
simple interfaces (no shared mutable state, no host-internal types), so an out-of-process proxy
can implement `IConnector` later without changing existing connectors.

1. Create `OctaGrid.Extensions.Abstractions`; introduce `IConnector`, `IPrinterSession` and the
   capability interfaces; adapt the existing `IDeviceClient` and factories behind them.
2. Replace the `PrinterProtocol` enum with connector ids. **Pre-alpha database policy:** the app has no
   stable release yet, so schema changes do **not** add new migrations. The single `InitialCreate`
   migration is regenerated instead, and no enum-to-id data mapping is written. The app does **not**
   back up, delete or recreate databases itself: a database created by an older `InitialCreate` must be
   removed manually by the developer or tester (`octagrid.db` in the OctaGrid data folder). From the
   first stable release on, normal additive migrations are used.
3. Move the Bambu and Moonraker code into separate projects loaded through the same extension manager.
4. Add the manifest, compatibility checks, extension states and the settings-schema driven "add
   printer" form.
5. Add user-folder installation and the trust prompt, then the Store (out-of-process) and Docker
   sources.
6. Add developer mode, the `octagrid-ext pack` command and `OctaGrid.Extensions.Testing`.
7. Publish the SDK package and a project template; add an SDK versioning section to `docs/VERSIONING.md`.
8. Create the `octagrid-extensions` index repository with CI validation, then the in-app catalog.

## Open questions

1. ~~**In-process only in v1, or out-of-process from the start?**~~ **Decided (2026-11-05): in-process
   in v1**, with contracts kept proxy-friendly so an out-of-process host can be added later (section 8).
2. ~~**Store add-ons:** can the app load code from add-on packages?~~ **Answered by the prototype:**
   not in place, but yes by copying into the host's data folder. Remaining checks (Store-signed
   package, Smart App Control, Store certification, arm64) are listed under "Not yet verified".
3. ~~**Distribution of third-party extensions**~~ **Decided (2026-11-06):** author-owned GitHub
   releases plus a curated index repository and in-app catalog (section 11).
4. ~~**Extension API version vs app version**~~ **Decided (2026-11-06):** one version line. The SDK is
   released with the app and extensions declare a host version range (section 5).
5. ~~**Naming**~~ **Decided (2026-11-06):** *Connector* for the kind, *Extension* for the package
   (section 9).
6. ~~**Store policy**~~ **Decided (2026-11-06):** follow the PowerToys Command Palette practice. Each
   extension is its own Store listing with an app extension declaration; the host app discovers it
   and does not download extension code. Command Palette ships this way in the Store, so it is the
   reference for certification.
