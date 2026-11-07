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

Extensions are loaded when OctaGrid starts. Restart OctaGrid after adding, changing or removing one.

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

The class needs a public parameterless constructor, or one that takes an `ILoggerFactory`.

### Host version ranges

A pre-release OctaGrid (for example `1.0.19-pre-alpha.3`) only matches a range that names a pre-release of the same
`major.minor.patch`, such as `>=1.0.19-pre-alpha`. A range like `>=1.0.0 <2.0.0` does not match it.

## States

| State | Meaning |
|---|---|
| Loaded | Available to printers. |
| Disabled | Turned off by the user. |
| Incompatible | The `host` range does not include this version of OctaGrid. The reason names both versions. |
| Failed | The manifest, the assembly or the connector could not be loaded, or the id is already used. The reason says why. |

One extension that is incompatible or fails never stops OctaGrid or the other extensions from loading.

## Isolation

Each extension is loaded into its own `AssemblyLoadContext`. The connector SDK (`OctaGrid.Extensions.Abstractions`),
`OctaGrid.Protocol`, `Microsoft.Extensions.*` and the .NET libraries come from OctaGrid, so `IConnector` is the same
type on both sides. Any other library the extension uses (for example an MQTT client) is loaded from the extension's
own folder, so it cannot clash with the versions OctaGrid or other extensions use.

Extensions run in the OctaGrid process with the same permissions as OctaGrid. Only install extensions you trust.

## Not available yet

- Uninstalling, installing from a file or a catalog, and a confirmation prompt that names the publisher.
- Loading without a restart, and a developer mode that watches a build folder.
- Out-of-process extensions (Microsoft Store packages).
