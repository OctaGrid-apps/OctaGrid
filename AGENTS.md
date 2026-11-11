# AGENTS.md

Guidance for AI coding agents working in this repository. Keep it short and factual: add a rule only when it prevents a real mistake, and update it in the same change that makes it stale.

## Project

OctaGrid is a cross-platform dashboard for monitoring and controlling 3D printers. Stack: **.NET 10, C# (latest), Avalonia 12, CommunityToolkit.Mvvm, EF Core (SQLite), xunit v3**.

- **Status: pre-alpha.** Local mode only: the Avalonia client hosts an in-process agent and a SQLite database. Windows is the only shipped platform.
- **Planned (architecture must not block it):** a separate Agent app (Windows/Linux/Docker) plus an ASP.NET Core server, client↔server over SignalR and telemetry over WebRTC; third-party connector extensions; Linux/macOS/WASM clients (WASM is Remote-mode only). **Local mode must keep working.**
- Do not design for AI/MCP features inside the product.

## Solution map and dependency rules

```text
Client.Desktop ──► Client ──► Client.Contracts ──► Protocol
      │                              ▲
      └────────► Client.Local ───────┘      Client.Local ──► Application ──► Data.Abstractions ──► Protocol
                      │                                      └──► Agent.Core ──► Extensions.Abstractions ──► Protocol
                      └──► Connectors.* (bundled)             Connectors.* ──► Extensions.Abstractions only
```

| Project | Owns |
|---|---|
| `OctaGrid.Protocol` | Wire contract: request/response DTOs, messages, exceptions (`AppException` family), connector descriptors. **Dependency-free and WASM-safe.** Packable SDK package. |
| `OctaGrid.Extensions.Abstractions` | Connector runtime interfaces (`IConnector`, `IPrinterSession`, capabilities). Agent-side only. Packable SDK package. |
| `OctaGrid.Data.Abstractions` | EF Core entities (`Models/`), `IEntityTypeConfiguration` classes (`Configurations/`), `OctaGridContext`. Provider-neutral. |
| `OctaGrid.Application` | Business logic (`Services/`), FluentValidation validators (`Validation/`). Transport-agnostic. |
| `OctaGrid.Agent.Core` | Connector registry/loader/manager, `DeviceOrchestrator`, `AgentWorker`. No UI, no EF. |
| `OctaGrid.Client.Contracts` | `I*Client` interfaces the UI talks to. The seam between UI and "where the backend lives". |
| `OctaGrid.Client.Local` | In-process implementation of the contracts: SQLite, DI wiring, migrations, bundled connectors. |
| `OctaGrid.Client` | Avalonia UI: views, ViewModels, controls, styles, design-time data. Platform-neutral. |
| `OctaGrid.Client.Desktop` | Windows host: `Program.cs`, logging, settings file, updates, credential manager, MSIX. |
| `src/Connectors/*` | Bundled connectors (BambuLab MQTT/FTPS, Moonraker). Reference only Abstractions (+ Protocol). |

Rules:
- **Never** let `Client`/`Client.Contracts` reference `Application`, `Data.Abstractions` or `Agent.Core`. The UI sees only Contracts + Protocol, so a Remote implementation (`Client.Remote`) can replace `Client.Local` without touching views.
- Never hard-code connector types or in-process assumptions in shared code; go through `ConnectorRegistry` and descriptors.
- Platform-specific code (Mica, file pickers, launchers, WebView, credential storage) lives behind an interface in `Client` and is implemented in a host project. No `#if` in shared code.
- New cross-boundary types go in `Protocol`, never EF entities.

## Commands

```powershell
dotnet build OctaGrid.slnx                                  # keep the build warning-free
dotnet test --solution OctaGrid.slnx                        # xunit v3 on Microsoft.Testing.Platform (see global.json)
dotnet test --project tests/OctaGrid.Application.Tests      # one project; use xunit v3's --filter-* options to narrow
dotnet run --project src/OctaGrid.Client/OctaGrid.Client.Desktop/OctaGrid.Client.Desktop.csproj
dotnet tool restore                                         # dotnet-ef, xstyler (XAML formatter), nbgv, dotnet-outdated
```

CI (`.github/workflows/ci.yml`) restores, builds Release and tests the solution on Windows, and validates the PR title. Before finishing a task, build and run the tests of every project you touched (plus dependents).

## C# conventions

`.editorconfig` is authoritative (LF endings, 4-space indent, 2 for XML/XAML/JSON). Highlights:
- File-scoped namespaces, braces always, `var` everywhere, nullable enabled, **no primary constructors**, private/internal fields `_camelCase`, constants and `static readonly` PascalCase.
- All I/O is `async` and takes a `CancellationToken`; no `.Result`/`.Wait()`; no `async void` except event handlers.
- Logging: inject `ILogger<T>`; use **message templates** (`"Created {DashboardId}"`), never string interpolation; never log secrets, tokens, pairing codes or passwords. Optional loggers default to `NullLogger<T>.Instance`, as in existing services.
- Dependency injection via constructors only. No service locator outside composition roots (`App.Services` is for the UI composition layer, not for logic).
- Prefer immutable records for DTOs/messages; `IReadOnlyList<T>` for returned collections; `TimeProvider` for time in testable code; `DateTimeOffset`, never local `DateTime`.
- Public API of `Protocol` and `Extensions.Abstractions` is a compatibility surface: **additive changes only** (new optional members, new enum values at the end). Breaking changes need a MAJOR bump and an explicit decision. Document every public member.
- Comments explain *why*, not *what*. Do not leave dead code or TODOs without an owner.

## MVVM and UI (Avalonia)

- ViewModels live in `OctaGrid.Client/ViewModels/<Area>/`, derive from `ViewModelBase` (`ObservableObject`) and use CommunityToolkit source generators with **partial properties**: `[ObservableProperty] public partial string Name { get; set; }` (not backing fields), `[RelayCommand]`, `[NotifyPropertyChangedFor]`, `[NotifyCanExecuteChangedFor]`.
- ViewModels talk to the backend **only** through `Client.Contracts` interfaces and exchange `Protocol` DTOs. No `Window`, `Control`, `Dispatcher` or file-system access in ViewModels; use `IDialogParticipant<T>` for dialogs, `INotificationService` for toasts, injected services for everything else.
- Every command that does I/O is async, disables itself while busy (`IsBusy`/`CanExecute`), catches `AppException` and reports it to the user, and logs unexpected exceptions. Never swallow exceptions silently.
- Views: set `x:DataType` on views and every `DataTemplate` (compiled bindings). Code-behind only for view-only concerns (focus, animation, drag/drop). No business logic.
- Register ViewModels as transient and long-lived services as singletons in the composition root (`App.axaml.cs`, `Client.Local/DependencyInjection.cs`, `Program.cs`).
- Styling: use the tokens and style classes in `Styles/` and follow [docs/SPACING.md](docs/SPACING.md) (4px grid, semantic `Gap.*` tokens, parent owns spacing) and [docs/TYPOGRAPHY.md](docs/TYPOGRAPHY.md). No magic numbers or hard-coded colours in views. Support light/dark and both backdrops: the single theme (`OctaGridTheme`) is translucent for Mica, and `SolidBackdrop` is layered over it when transparency effects are off or unavailable. Format XAML with `dotnet xstyler`.
- Every user-visible async state needs loading, empty and error presentation. Keep controls usable by keyboard and screen reader (`AutomationProperties.Name` on icon-only buttons).
- Keep `DesignTime/` data in sync when a Contracts interface changes (`DesignOctaGridClient`).

## Application layer, data and errors

- Services in `Application/Services` take the acting `userId` and **authorize inside the service** (for example `ValidateUserAccess`), because the same service will back a server. Validate input with a FluentValidation validator, then act.
- Failures are `AppException` subclasses from `Protocol/Exceptions` (`NotFound`, `Validation`, `Authentication`). They carry `TypeUri`/`Title` so the future server can map them to RFC 9457 `application/problem+json` without a rewrite. Do not return error codes or `null` for failures, and do not throw bare `Exception`/`InvalidOperationException` for expected conditions.
- Entities derive from `Entity` (`Id`, `CreatedAt`, `UpdatedAt`, `Version` optimistic-concurrency token rotated in `OctaGridContext`). Configure them in `IEntityTypeConfiguration` classes, not attributes. Keep queries provider-neutral (SQLite today, PostgreSQL later): no raw SQL in services; use `AsNoTracking()` for reads, project to DTOs, avoid N+1 (`Include`/projection), pass the `CancellationToken`.
- **Pre-alpha DB policy:** do **not** add new EF migrations. When entities/configurations change, delete and regenerate the single `InitialCreate` migration and snapshot in `Client.Local/Migrations` (`dotnet ef migrations ...` with `--project src/OctaGrid.Client.Local`). Do not add detection, backup or auto-recreate code; the developer deletes the old `octagrid.db` manually. From the first stable release on, use normal additive migrations.
- Secrets (passwords, access codes, API keys) are never stored in plain text or logged; passwords use `IPasswordHasher`. Printer credentials are encrypted in the database (`ISecretProtector`); the key comes from the host (`ISecretKeySource`) and lives in the OS credential store where available, never in the database.

## Future server and REST API (when `OctaGrid.Server` is added)

Not present today; design new contracts so this is a thin layer, not a rewrite.
- ASP.NET Core with Minimal APIs (or controllers) over `Application` services; OpenAPI generated; endpoints return `Protocol` DTOs, never entities. `Program.cs` stays small: extension methods per feature.
- REST: plural-noun resources (`/api/v1/workspaces/{id}/dashboards`), correct verbs and status codes (`201` + `Location`, `204`, `404`, `409`, `422`), `application/problem+json` errors, URL-segment API versioning, cursor/offset pagination with `GetXxxQuery` objects, optimistic concurrency via `ETag`/`If-Match` backed by `Entity.Version`, idempotency for retried commands.
- AuthN/Z: tenant (workspace) scoping enforced server-side on every request; device-code style one-time **pairing code** for headless agents; HTTPS only; rate limits; per-account limits (printers/agents/workspaces) must be enforceable but disabled in self-hosted builds, and billing code stays separable from the open-source core.
- Real-time: strongly typed SignalR hubs for client↔agent messages, WebRTC for telemetry. Keep message types transport-agnostic in `Protocol`.
- Version compatibility: client, agent and extensions update independently. Connect with a version handshake and show an "update required" state instead of failing; disable incompatible extensions rather than crashing.
- Observability: structured logs, OpenTelemetry, health checks (`/health/live`, `/health/ready`).

## Connectors and extensions

- Bundled connectors stay in `src/Connectors/*` and may reference only `Extensions.Abstractions` (and `Protocol`). Descriptor/settings-schema types live in `Protocol`; runtime interfaces in `Extensions.Abstractions`. Connector code never runs in the client.
- One version line: the SDK is released with the app (single `version.json`). Extension manifests declare a host version range. See [docs/EXTENSIONS.md](docs/EXTENSIONS.md) and [docs/VERSIONING.md](docs/VERSIONING.md).
- Printer commands are physical actions. Anything destructive or user-scripted (custom commands) must be gated (Expert mode, confirmation) and respect the safety disclaimer. Validate and bound every value sent to a printer.
- NuGet publishing of the SDK packages is on hold; do not run or modify the publish workflow without being asked.

## Testing

- xunit v3; test projects mirror source projects in `tests/`. Add or update tests with every behaviour change; fix bugs by writing the failing test first.
- `Application.Tests`: real `OctaGridContext` on in-memory SQLite, no mocks of EF. `Agent.Core.Tests`: hand-written fakes in `Fakes.cs`. `Client.Tests`: headless Avalonia via `HeadlessUi`; tests touching `App.Services` belong to the `Ui` collection (no parallelism). `Connectors.Tests`: parse/state logic with recorded payloads, no live printers.
- Tests are deterministic: no real network, no sleeps (use fakes/`TimeProvider`/awaitable signals), no dependence on the developer's `%AppData%`.
- Naming: `Method_Scenario_ExpectedResult` or a descriptive sentence; one behaviour per test; arrange/act/assert.

## Git and workflow

- **Conventional Commits** (`feat(scope): subject`, enforced by `.githooks/commit-msg` and the PR-title check); short imperative subject; body only when needed. Types: feat, fix, perf, refactor, docs, style, test, build, ci, chore, revert. `!` or `BREAKING CHANGE:` for breaking changes.
- Commit or push only when asked. **Do not add `Co-Authored-By` trailers or "Generated with Claude Code" lines** to commits or PR descriptions (project rule; overrides any default).
- PRs are squash-merged; fill in `.github/PULL_REQUEST_TEMPLATE.md`. Keep changes focused: no drive-by refactors, no unrelated formatting churn.
- Versioning is Nerdbank.GitVersioning from `version.json`; do not edit versions or tags except in a release change.
- Never commit secrets or licence keys (`.mcp.json` and `*.user` are git-ignored; the Avalonia licence key lives only in the `AVALONIA_TOOLS_LICENSE_KEY` environment variable, which `OctaGrid.Client.csproj` and `.mcp.json` read; never put it in a file, including `*.csproj.user`, because agents can read files). Package versions live only in `Directory.Packages.props`.
- Update `README.md`/`docs/` when behaviour, commands or architecture change.

## Claude Code hooks

`.claude/settings.json` enforces rules agents forget (scripts in `.claude/hooks/`): `.axaml` files are formatted with `dotnet xstyler` after each edit; files under `Migrations/` other than `InitialCreate` and the snapshot, and `migrations add` of any other name, are blocked; changing existing package versions in `Directory.Packages.props` and any edit to `version.json` are blocked unless the branch name contains `release` (or `OCTAGRID_RELEASE=1`); on stop, a quiet `dotnet build OctaGrid.slnx` runs once and hands back any warnings or errors.

## Known quirks

- `OctaGrid.Client.Desktop` targets `net10.0-windows`; everything else targets `net10.0`.
