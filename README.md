# OctaGrid

OctaGrid is a modern, cross-platform desktop application for managing and monitoring 3D printers from a single dashboard. Built with [.NET 10](https://dotnet.microsoft.com/) and [Avalonia UI](https://avaloniaui.net/).

> [!WARNING]
> **Pre-Alpha Release**: OctaGrid is currently in early pre-alpha. Features and local database schemas may change between releases. Please report any bugs or suggestions in [GitHub Issues](https://github.com).

---

## Features

- **Multi-Printer Dashboard**: Monitor multiple printers simultaneously with real-time temperature graphs, progress bars, and print statuses.
- **Local-First Architecture**: Runs fully locally with an embedded in-process agent and SQLite database—no cloud connection required.
- **File Management & G-code Preview**: Browse, upload, delete, and inspect files on your printers with embedded thumbnail generation.
- **Modern Fluent Design**: Native Windows 11 Mica backdrop support, Dark/Light themes, and responsive navigation.

---

## Supported Printers & Protocols

| Protocol | Firmware / Ecosystem | Connection Type |
|---|---|---|
| **Bambu Lab MQTT** | Bambu Lab (X1, P1, A1 series) | Local Network (MQTT + FTPS) |
| **Moonraker** | Klipper | Local Network (WebSocket JSON-RPC + HTTP) |

---

## Getting Started

### Download Pre-Alpha

Pre-compiled standalone binaries for Windows (`win-x64`) are available on the [Releases](https://github.com) page.
- Download `OctaGrid-win-x64.zip`.
- Extract the archive and launch `OctaGrid.Client.Desktop.exe`.
- All local data and printer profiles are safely stored in `%LocalAppData%\OctaGrid\octagrid.db`.

---

## Building from Source

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- Visual Studio 2026, JetBrains Rider, or VS Code with C# Dev Kit

### Clone & Build

```powershell
git clone https://github.com/your-username/OctaGrid.git
cd OctaGrid
dotnet build OctaGrid.slnx
```

### Run Desktop Client

```powershell
dotnet run --project src/OctaGrid.Client/OctaGrid.Client.Desktop/OctaGrid.Client.Desktop.csproj
```

---

## Development Setup

### Avalonia Accelerate License Key

The client project references `AvaloniaUI.DiagnosticsSupport`, which uses an `AvaloniaUILicenseKey`. To keep your license key personal and untracked by Git, store it in an `OctaGrid.Client.csproj.user` file (which MSBuild imports automatically and Git ignores via `*.user` in `.gitignore`):

1. Create `src/OctaGrid.Client/OctaGrid.Client/OctaGrid.Client.csproj.user` next to `OctaGrid.Client.csproj`.
2. Add your license key:

```xml
<Project>
  <ItemGroup>
    <AvaloniaUILicenseKey Include="avln_on_key:v1:YOUR_LICENSE_KEY_HERE" />
  </ItemGroup>
</Project>
```

---

## Commit Conventions

OctaGrid follows the [Conventional Commits](https://www.conventionalcommits.org/) specification for all git commits. This enables automated release notes and changelog generation.

### Format

```text
<type>(<optional scope>): <description>

[optional body]

[optional footer(s)]
```

### Commit Types

| Type | Description | Example |
|---|---|---|
| `feat` | A new user-facing feature or capability | `feat(dashboard): add real-time temperature graph` |
| `fix` | A bug fix | `fix(agent): reconnect WebSocket on disconnect` |
| `refactor` | Code restructuring without changing behavior or adding features | `refactor(client): simplify message handler` |
| `perf` | Performance improvement or optimization | `perf(ui): optimize printer card redraws` |
| `style` | Formatting, spacing, or visual theme polish | `style(theme): update Mica theme border radius` |
| `build` | Changes to build scripts, dependencies, or packaging | `build(msix): configure makepri automatic indexing` |
| `ci` | Changes to CI/CD workflows and automation scripts | `ci(github): add automated commitlint action` |
| `docs` | Documentation updates (README, docs) | `docs: document commit conventions and git hooks` |
| `test` | Adding or correcting unit/integration tests | `test(sync): add tests for local agent synchronizer` |
| `chore` | Miscellaneous maintenance tasks | `chore: update .gitignore and cleanup unused files` |

### Breaking Changes

Append an exclamation mark (`!`) after the type/scope or include `BREAKING CHANGE:` in the commit footer:
```text
feat(api)!: migrate printer pairing protocol to v2
```

### Local Git Hook Setup

The repository includes a pre-configured hook in `.githooks/commit-msg` that validates commit messages locally before commits are created.

To enable it, run once in your cloned repository:

```powershell
git config core.hooksPath .githooks
```

---

## Versioning & Release Process

OctaGrid uses [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) for single-source, git-commit-height versioning.

### How to Bump the Version Before a Release

1. **Update `version.json`**:
   Open the root [`version.json`](version.json) file and adjust the `version` property:
   - **Pre-Alpha / Alpha**: `"version": "0.1-alpha"` or `"version": "0.2-alpha"`
   - **Beta**: `"version": "1.0-beta"`
   - **Release Candidate / Stable**: `"version": "1.0"`

   *(Alternatively, use the global CLI tool: `dotnet tool install -g nbgv` then run `nbgv bump minor` or `nbgv prepare-release`.)*

2. **Commit the Change**:
   ```powershell
   git add version.json
   git commit -m "Bump version to 0.2-alpha"
   ```

3. **Automatic Synchronization on Build**:
   When you run `dotnet build` or `dotnet publish`:
   - Nerdbank.GitVersioning dynamically generates the 4-part numeric assembly and file versions (`Major.Minor.{commitHeight}.0`).
   - The desktop project's build target automatically updates `Identity.Version` in [`Package.appxmanifest`](src/OctaGrid.Client/OctaGrid.Client.Desktop/Package.appxmanifest) to match the new version.

4. **Tag the Release in Git**:
   ```powershell
   git tag v0.1.0-alpha.1
   git push origin main --tags
   ```

---

## License

This project is licensed under the [MIT License](LICENSE.txt).
