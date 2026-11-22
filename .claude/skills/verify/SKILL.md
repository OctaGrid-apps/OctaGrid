---
name: verify
description: Build, launch and drive the OctaGrid desktop app (Local mode) to observe a change at runtime.
---

# Verify OctaGrid at runtime

## Build and launch

```bash
dotnet build src/OctaGrid.Client/OctaGrid.Client.Desktop/OctaGrid.Client.Desktop.csproj -v q -nologo
# launch detached (Git Bash); the exe path is the net11.0-windows* output folder
(src/OctaGrid.Client/OctaGrid.Client.Desktop/bin/Debug/net11.0-windows10.0.19041.0/OctaGrid.Client.Desktop.exe >/dev/null 2>&1 &)
```

## Gotchas

- The app uses the developer's real data in `%AppData%\OctaGrid` (`octagrid.db`, `logs/`), and it can't be
  redirected. Copy `octagrid.db` before driving anything that writes. Close the app, then copy it back
  and delete any `-wal`/`-shm` files.
- Safe printers: pick the "Demo printer (simulated)" connector (sample extension). Never drive real printers.
- Evidence: `%AppData%\OctaGrid\logs\octagrid-<date>[_NNN].log` (newest file). `LocalAgentSynchronizer` logs
  each printer `EntityChanged`; `AgentWorker`/`PrinterOrchestrator` log what the agent applied.
- DB inspection: no sqlite3/python on the machine. Use a file-based app with `#:package Microsoft.Data.Sqlite`
  and `Mode=ReadOnly` (`dotnet run q.cs -- "<sql>"`) from a scratch folder.

## Driving the UI

- Avalonia DevTools MCP: `attach-to-app`, then `search` with `searchTemplates: true` (ToggleSwitch, Button,
  ListBoxItem, TextBox, ComboBox), `input` Click/Text, `set-prop` (e.g. connector `SelectedIndex`,
  `WindowState=Normal` to restore a minimized window). Node ids change when lists reload, so search again.
- `MenuFlyout` popups (a card's "More options": Edit/Delete/Rename) aren't in the DevTools tree. Use computer use
  (grant `octagrid.client.desktop.exe`) to click them. Confirmation dialogs' buttons are visible to DevTools.
- Local mode hides the Agents and Workspaces nav items (`IsRemoteMode`). To reach Workspaces, `set-prop IsVisible=True`
  on the 4th `ListBoxItem` in the rail's top list, then click it.
