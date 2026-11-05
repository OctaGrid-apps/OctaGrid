# Store extension prototype

Answers open question 2 of [ADR 0001](../../docs/adr/0001-connector-extension-api.md): can a packaged
OctaGrid host discover third-party Store extension packages and load their code in-process?
This is throwaway prototype code. It is not part of `OctaGrid.slnx`.

| Project | Role |
|---|---|
| `Prototype.Abstractions` | Stand-in for the SDK: `IConnector`, `ConnectorDescriptor`, API version. |
| `Sample.Connector` | The third-party extension. Its own MSIX with a `windows.appExtension` declaration (contract `octagrid.connector`). The exe is only there because a package needs an `Application`. |
| `Probe.Host` | The host. Its MSIX declares `windows.appExtensionHost` and the alias `octagrid-probe`. It lists extensions, checks API compatibility and tries four ways of loading the DLL. |

## Run

```powershell
.\prototypes\store-extensions\build-prototype.ps1
Add-AppxPackage artifacts\prototype\OctaGrid.Prototype.Host.msix
Add-AppxPackage artifacts\prototype\OctaGrid.Prototype.SampleConnector.msix
octagrid-probe
```

The build signs with the OctaGrid developer certificate from `Cert:\CurrentUser\My` (created by
`build-msix.ps1`), which must also be trusted in `LocalMachine\TrustedPeople` to install. The output
is also written to `%TEMP%\octagrid-probe.log` (virtualised for the packaged host).

## Remove

```powershell
Get-AppxPackage OctaGrid.Prototype.* | Remove-AppxPackage
```

## Result (2026-11-05, signed with the developer certificate)

| Variant | Result |
|---|---|
| Discover extension, read properties | works |
| Load DLL from the package install folder | **Access is denied** |
| Load DLL from the extension's `PublicFolder` | **Access is denied** |
| Copy package files to the host's data folder, load the copy | **works** (SDK shared from the default context); would need signed DLLs under Smart App Control |
| Start the extension's own exe through its execution alias (`--serve`, stdio) | **works**, nothing copied, runs from its own package folder |

The access-denied is not caused by sideloading: package folders under `WindowsApps` give other
processes read-only access, and only the package itself may execute (identical ACLs for the
developer-signed sample and a Store package; loading a Store app's DLL fails the same way).

See the ADR for what this means and what is still unverified (Store-signed package, Smart App
Control, Store certification).
