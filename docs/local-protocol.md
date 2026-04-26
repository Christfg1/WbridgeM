# Local Protocol

This project uses a simple local API plus two WebSocket streams.

## Transport

- Base URL: `http://<windows-ip>:5055`
- WebSocket URL: `ws://<windows-ip>:5055/ws`
- Input Bridge WebSocket URL: `ws://<windows-ip>:5055/ws/input`
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

## WebSocket Events

### `status-updated`

Sent on a timer from the Windows host.

### `clipboard-updated`

Sent when the Windows clipboard changes locally or through the API.

### `command-completed`

Sent after a command run finishes.

## Input Bridge WebSocket

`/ws/input` is dedicated to Input Bridge traffic so mouse and keyboard forwarding stays separate from status and clipboard updates.

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

Supported event kinds:

- `MouseMove`
- `MouseButton`
- `Scroll`
- `Key`

## Folder Model

The Windows host treats one folder as the transfer root:

- default: `%USERPROFILE%\Documents\BridgeDrop`

Uploads land there, and downloads are restricted to paths inside that root.

## Safety Model

The v1 safety rules are intentionally lightweight:

- command execution requires a `confirmed: true` request
- the Mac UI always previews before running
- the Windows host blocks a few known-destructive command tokens
- Input Bridge requires explicit opt-in and macOS Accessibility permission on the Mac side
- the Windows host releases held input state when the Input Bridge session ends
- all traffic stays on the local network unless someone explicitly runs a networked shell command
