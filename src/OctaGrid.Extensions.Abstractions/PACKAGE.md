# OctaGrid.Extensions.Abstractions

The contracts for writing extensions for [OctaGrid](https://github.com/OctaGrid-apps/OctaGrid), the 3D printer dashboard: implement `IConnector` and `IPrinterSession`, describe the extension in an `octagrid-extension.json` manifest and package it as an `.octaext`.

- Guide: [docs/EXTENSIONS.md](https://github.com/OctaGrid-apps/OctaGrid/blob/main/docs/EXTENSIONS.md)
- Working example: [samples/OctaGrid.Extensions.Sample](https://github.com/OctaGrid-apps/OctaGrid/tree/main/samples/OctaGrid.Extensions.Sample)

The package version follows the app version. An extension names the app versions it supports in its manifest.
