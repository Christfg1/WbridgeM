# Local Protocol

This project uses a simple local API plus two WebSocket streams.

## Transport

- Base URL: `http://<windows-ip>:5055`
- Main WebSocket URL: `ws://<windows-ip>:5055/ws`
- Reverse input WebSocket URL: `ws://<windows-ip>:5055/ws/input`
- Auth header: `X-Bridge-Secret: <shared secret>`

## HTTP Endpoints

### `GET /api/health`

Public health probe.

Response:

```json
{
  "status": "ok",
  "appVersion": "1.0.0"
}
```

### `GET /api/bridge`

Returns host metadata for the connected Windows machine.

Response includes:

- `hostName`
- `appVersion`
- `localAddresses`
- `port`
- `storageRoot`
- `webSocketPath`
- `connectedMacClients`

### `GET /api/status`

Returns the latest Windows metrics snapshot:

- `cpuLoadPercent`
- `memoryUsedGb`
- `memoryTotalGb`
- `diskUsedGb`
- `diskTotalGb`
- `diskFreeGb`
- `gpu`

### `GET /api/clipboard`

Returns the current Windows clipboard text.

### `POST /api/clipboard`

Sets the Windows clipboard text.

Request:

```json
{
  "text": "hello from macOS",
  "sourceDevice": "mac"
}
```

### `GET /api/files`

Lists files below the configured Windows shared folder.

### `POST /api/files/upload`

Multipart form upload:

- `file`: required
- `subdirectory`: optional

### `GET /api/files/download?relativePath=<path>`

Downloads one file from the Windows shared folder.

### `POST /api/commands/preview`

Request:

```json
{
  "command": "Get-ChildItem $env:USERPROFILE\\Documents",
  "shell": "PowerShell"
}
```

Response includes:

- `blocked`
- `blockedReason`
- `warnings`

### `POST /api/commands/run`

Request:

```json
{
  "command": "Get-ChildItem $env:USERPROFILE\\Documents",
  "shell": "PowerShell",
  "confirmed": true
}
```

### `GET /api/input/control-mac`

Returns the Windows-primary control state.

Response:

```json
{
  "enabled": true,
  "phase": "Armed",
  "activationEdge": "Right",
  "escapeHotkey": "Ctrl + Alt + Windows + Esc",
  "requiresMacAccessibilityPermission": true
}
```

### `POST /api/input/control-mac`

Enables or disables the Windows-to-Mac control mode.

Request:

```json
{
  "enabled": true
}
```

## Main WebSocket Events

The main `/ws` socket carries status, clipboard, command, and Windows-to-Mac input bridge events.

Each main WebSocket message uses this envelope:

```json
{
  "type": "status-updated",
  "occurredAt": "2026-04-26T19:41:12.521Z",
  "payload": {}
}
```

### `status-updated`

Sent on a timer from the Windows host.

### `clipboard-updated`

Sent when the Windows clipboard changes locally or through the API.

### `command-completed`

Sent after a command run finishes.

### `control-mac-state`

Sent when the Windows-primary control mode changes phase.

Message shape:

```json
{
  "type": "control-mac-state",
  "occurredAt": "2026-04-26T19:41:12.521Z",
  "payload": {
    "enabled": true,
    "phase": "Active",
    "activationEdge": "Right",
    "escapeHotkey": "Ctrl + Alt + Windows + Esc",
    "requiresMacAccessibilityPermission": true
  }
}
```

### `control-mac-input`

Sent while Windows is actively controlling the Mac.

Message shape:

```json
{
  "type": "control-mac-input",
  "occurredAt": "2026-04-26T19:41:12.521Z",
  "payload": {
    "kind": "MouseMove",
    "deltaX": 14,
    "deltaY": -3,
    "button": null,
    "isDown": null,
    "scrollX": 0,
    "scrollY": 0,
    "windowsVirtualKey": null
  }
}
```

Supported input kinds:

- `MouseMove`
- `MouseButton`
- `Scroll`
- `Key`

## Reverse Input WebSocket

`/ws/input` remains dedicated to the reverse Mac-to-Windows input path so that the older direct injection flow stays isolated from the main bridge socket.

Message shape:

```json
{
  "type": "input-bridge-event",
  "payload": {
    "kind": "MouseMove",
    "deltaX": 14,
    "deltaY": -3,
    "button": null,
    "isDown": null,
    "scrollX": 0,
    "scrollY": 0,
    "windowsVirtualKey": null
  }
}
```

Supported input kinds:

- `MouseMove`
- `MouseButton`
- `Scroll`
- `Key`

## Folder Model

The Windows host treats one folder as the transfer root:

- default: `%USERPROFILE%\\Documents\\BridgeDrop`

Uploads land there, and downloads are restricted to paths inside that root.

## Safety Model

The v1 safety rules are intentionally lightweight:

- command execution requires a `confirmed: true` request
- the Mac UI always previews before running
- the Windows host blocks a few known-destructive command tokens
- `Control Mac from Windows` requires explicit opt-in and macOS Accessibility permission on the Mac side
- the reverse Mac-to-Windows input path also requires explicit opt-in and macOS Accessibility permission on the Mac side
- both sides release held input state when their active control session ends
- all traffic stays on the local network unless someone explicitly runs a networked shell command
