# Glossary

The words OctaGrid's code, UI and docs use, and what each one means. Use these words for these things, and only these. When the code and this list disagree, fix one of them in the same change.

## Workspaces and people

| Term | Meaning | In code |
|---|---|---|
| Workspace | The unit of sharing: its members, printers, agents and dashboards. Local mode has one. | `Workspace` (aggregate root) |
| Member | A user's membership of a workspace, with a role. | `WorkspaceMember` |
| Role | Owner, Admin, Operator or Reader. Decides permissions; never tested directly. | `Role`, `RolePermissions` |
| Permission | What a member may do (`View`, `ControlPrint`, ...). Services and the UI ask for these. | `Permission` |
| Dashboard | A view of a workspace's printers. A workspace always has one, and exactly one is its default. | `Dashboard` (in the workspace aggregate) |

## Printers

| Term | Meaning | In code |
|---|---|---|
| Printer | A 3D printer as OctaGrid knows it: name, connector, settings, secrets, its agent. Never "device". | `Printer` (aggregate root) |
| Printer state | What a printer is doing, in one vocabulary for every connector: `Offline`, `Connecting`, `Idle`, `Preparing`, `Printing`, `Paused`, `Completed`, `Cancelled`, `Failed` (the job ended badly) and `Error` (the printer is faulted). | `UniversalPrinterState` |
| Telemetry | The live snapshot of a printer (state, temperatures, progress), streamed only for printers someone shows. | `PrinterTelemetry` |
| Status | The last printer state the backend kept, from state changes, so it is known without telemetry. | `PrinterStatus`, `PrinterResponse.State` |
| State change | An agent's report that a printer's state changed. Sent for every printer. | `PrinterStateChanged` |
| Print job | One print, from when a printer starts printing a file to its outcome. | `PrintJob` |
| Outcome | How a print job ended: `Completed`, `Failed`, `Cancelled`, or `Unknown` when the end was not seen. | `PrintJobOutcome` |
| Secrets | A printer's write-only settings (API keys, access codes), stored encrypted. | `PrinterSecrets`, `Printer.ProtectedSecrets` |

## Agents and connectors

| Term | Meaning | In code |
|---|---|---|
| Agent | The process that connects to printers and runs connectors. Local mode runs one in-process. | `Agent`, `AgentWorker` |
| Routing | Sending a user's request about a printer to that printer's agent, and only with the permission it needs. | `PrinterRouting` |
| Profile | What an agent is told about a printer so it can connect: connector, settings and secrets. | `PrinterProfile` |
| Session | An agent's live connection to one printer, created by its connector. | `IPrinterSession`, `PrinterOrchestrator` |
| Connector | The integration that talks to one kind of printer (Moonraker, Bambu Lab). | `IConnector`, `Printer.ConnectorId` |
| Extension | The installable unit that provides connectors. Built-in extensions ship with the app. | `Extension*` |

## Events

| Term | Meaning | In code |
|---|---|---|
| Domain event | A fact an aggregate recorded about itself, delivered after it is saved. | `DomainEvent` |
| Outbox | Where domain events wait, saved with the change that raised them, until delivered. | `OutboxMessage`, `OutboxDispatcher` |
| Notification | Something an agent reports on its own (telemetry, a state change, a console line). | `AgentNotification` |

## Words to avoid

- **Device** for a printer. "Device" appears only where a vendor protocol uses it inside a connector (Bambu Lab's `device/{serial}` MQTT topics) and for the machine an agent is paired from (`PairingCode.DeviceId`).
- **Connection state** for a printer's state.
- **Plugin** for an extension, and **extension** for a connector.
