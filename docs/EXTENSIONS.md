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

## Not available yet

- Installing from a catalog, and updates.
- Loading without a restart, and a developer mode that watches a build folder.
- Out-of-process extensions (Microsoft Store packages).

## Try it: the sample extension

`samples/OctaGrid.Extensions.Sample` is a connector for a simulated printer. It needs no hardware, so it is the quickest
way to try installing, trusting and removing an extension. It also shows what a small connector looks like (settings,
print control, files and a custom command).

1. Build it:
   ```bash
   dotnet build samples/OctaGrid.Extensions.Sample
   ```
   This writes the manifest and creates the package in `samples/OctaGrid.Extensions.Sample/bin/Debug/net10.0/package/`:
   the folder `octagrid-samples.demo-printer` and the same thing zipped as `OctaGrid.Extensions.Sample.octaext`.
2. In OctaGrid open **Settings > Extensions** and choose **Install from file…** (the `.octaext`) or **Install from
   folder…** (the `octagrid-samples.demo-printer` folder). The extension appears as *Needs approval*.
3. Choose **Review…**, read the prompt and **Trust and turn on**. *Demo printer (simulated)* is now active.
4. Add a printer (Printers > add) and pick *Demo printer (simulated)* as its connector. Set *Simulation speed* to **Fast**
   to see a whole print in seconds. Start a file such as `benchy.gcode`, then pause, resume or stop it. The *Say hello*
   custom command answers with a message.
5. Choose **Remove** to take it out again. It is turned off at once and its folder is deleted at the next start.

To see the other states, edit the installed copy of `octagrid-extension.json` (in `extensions/octagrid-samples.demo-printer/1.0.0/`)
and restart OctaGrid: set `"host": ">=9.0.0"` for *Incompatible*, or a wrong `"entryType"` for *Failed* after you approve it.
The sample's `host` range starts at the OctaGrid version it was built with, so rebuild it after the version changes.
