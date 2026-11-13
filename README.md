# OctaGrid

[![Get it from Microsoft](https://get.microsoft.com/images/en-us%20dark.svg)](https://apps.microsoft.com/detail/9NWQ15C73TXL?mode=direct)

OctaGrid is a modern, cross-platform desktop application for managing and monitoring 3D printers from a single dashboard. Built with [.NET 11](https://dotnet.microsoft.com/) and [Avalonia UI](https://avaloniaui.net/).

> [!WARNING]
> **Pre-Alpha Release**: OctaGrid is currently in early pre-alpha. Features and local database schemas may change between releases. Please report any bugs or suggestions in [GitHub Issues](https://github.com/OctaGrid-apps/OctaGrid/issues).

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

### 1. Install via Microsoft Store (Recommended)

Get the latest version directly from the Microsoft Store for seamless background updates and zero SmartScreen warnings:

<a href="https://apps.microsoft.com/detail/9NWQ15C73TXL?mode=direct">
  <img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200" alt="Get it from Microsoft Store" />
</a>

### 2. Download from GitHub Releases

Pre-compiled binaries for Windows (`win-x64`) are available on the [Releases](https://github.com/OctaGrid-apps/OctaGrid/releases) page:
- **Installer**: Download and run the `*-Setup.exe` file (includes built-in auto-updates).
- **Portable**: Download `OctaGrid-win-Portable.zip`, extract the archive, and launch `OctaGrid.exe`. The portable copy updates itself like the installed one.

> [!NOTE]
> Releases marked **Pre-release** (for example `1.0.17-pre-alpha`) are early builds for testers. The Microsoft Store only receives stable releases. See [Versioning & Releases](docs/VERSIONING.md).

All local data, configuration, and printer profiles are safely stored in `%AppData%\OctaGrid` (`%AppData%\OctaGrid\octagrid.db`). Printer passwords, access codes and API keys are encrypted in that file. The encryption key is kept in the Windows Credential Manager, so a copy of the file alone does not reveal them, and they have to be entered again on another machine.

---

## Building from Source

### Prerequisites

- [.NET 11.0 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) or later
- Visual Studio 2026, JetBrains Rider, or VS Code with C# Dev Kit

### Clone & Build

```powershell
git clone https://github.com/OctaGrid-apps/OctaGrid.git
cd OctaGrid
dotnet build OctaGrid.slnx
```

### Run Desktop Client

```powershell
dotnet run --project src/OctaGrid.Client/OctaGrid.Client.Desktop/OctaGrid.Client.Desktop.csproj
```

---

## Documentation

| Document | Contents |
|---|---|
| [Versioning & Releases](docs/VERSIONING.md) | Version scheme, release channels, release process, auto-updates |
| [Spacing guidelines](docs/SPACING.md) | Spacing tokens and layout style classes |
| [Typography guidelines](docs/TYPOGRAPHY.md) | Type scale, text roles and style classes |

---

## Development Setup

### Avalonia Accelerate License Key

The client project references `AvaloniaUI.DiagnosticsSupport`, which needs an Avalonia license key. The key is read from the `AVALONIA_TOOLS_LICENSE_KEY` environment variable (see `OctaGrid.Client.csproj`). Set it once for your user account, then restart your IDE, terminal and agent so they pick it up:

```powershell
[Environment]::SetEnvironmentVariable("AVALONIA_TOOLS_LICENSE_KEY", "avln_on_key:v1:YOUR_LICENSE_KEY_HERE", "User")
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
| `ci` | Changes to CI/CD workflows and automation scripts | `ci(github): add build and test workflow` |
| `docs` | Documentation updates (README, docs) | `docs: document commit conventions and git hooks` |
| `test` | Adding or correcting unit/integration tests | `test(sync): add tests for local agent synchronizer` |
| `chore` | Miscellaneous maintenance tasks | `chore: update .gitignore and cleanup unused files` |
| `revert` | Reverts a previous commit | `revert: feat(dashboard): add real-time temperature graph` |

Pull request titles follow the same format: pull requests are squash-merged, the PR title becomes the commit subject, and CI validates it.

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

OctaGrid follows [Semantic Versioning 2.0](https://semver.org) with pre-release channels
(`1.0.17-pre-alpha`, `1.1.0-preview.1`, `1.1.0`). [`version.json`](version.json) is the single
source of truth; release tags must be `v` + that version. See
**[docs/VERSIONING.md](docs/VERSIONING.md)** for channels, the release process and auto-update behaviour.

---

## License

This project is licensed under the [MIT License](LICENSE.txt).
