# Mac + Windows Bridge v1

Local-only bridge app for a Mac client and a Windows host on the same LAN.

Version 1 includes:

- Shared clipboard between Mac and Windows
- File transfer into and out of a Windows shared folder
- Windows status dashboard for CPU, RAM, disk, and GPU details when available
- Remote command runner with preview and confirmation
- Input Bridge Mode for mouse, scroll, and common keyboard forwarding from Mac to Windows
- Local HTTP + WebSocket communication with a shared secret

## Project Structure

```text
.
|-- README.md
|-- docs/
|   |-- input-bridge.md
|   `-- local-protocol.md
|-- mac/
|   `-- BridgeMac/
|       |-- Package.swift
|       `-- Sources/BridgeMacApp/
|           |-- BridgeMacApp.swift
|           |-- Models/
|           |-- Services/
|           |-- ViewModels/
|           `-- Views/
`-- windows/
    `-- BridgeWindowsHost/
        |-- BridgeWindowsHost.csproj
        |-- Program.cs
        |-- appsettings.json
        |-- Models/
        `-- Services/
```

## How It Works

The Windows app hosts a small LAN-only API on port `5055` by default. The Mac SwiftUI app connects to that host over HTTP for request/response actions, opens a WebSocket for live status and clipboard events, and uses a second dedicated WebSocket for Input Bridge traffic when remote control is active.

The first version keeps the trust model simple:

- Both devices must be on the same local network.
- The Mac user enters the Windows IP address manually.
- Every API request includes `X-Bridge-Secret`.
- Commands must be previewed and explicitly confirmed in the Mac UI.
- The Windows host blocks a few destructive command tokens by default.
- Input Bridge must be enabled explicitly in the Mac UI before it will capture any input.

## Run The Windows Host

Prerequisites:

- Windows 10 or 11
- .NET 8 SDK
- PowerShell available on the machine

Steps:

1. Open [windows/BridgeWindowsHost/appsettings.json](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/windows/BridgeWindowsHost/appsettings.json) and change `SharedSecret`.
2. Optional: change `Port` or `StorageRoot` if you want a different shared folder.
3. Open a terminal in [windows/BridgeWindowsHost](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/windows/BridgeWindowsHost).
4. Run:

```powershell
dotnet restore
dotnet run
```

5. Allow the inbound firewall prompt for the chosen port if Windows asks.
6. Note the printed LAN address, for example `http://192.168.1.42:5055`.

What the Windows app exposes:

- `GET /api/status`
- `GET/POST /api/clipboard`
- `GET /api/files`
- `POST /api/files/upload`
- `GET /api/files/download`
- `POST /api/commands/preview`
- `POST /api/commands/run`
- `GET /api/health`
- `GET /ws`
- `GET /ws/input`

## Run The Mac App

Prerequisites:

- macOS 13+
- Xcode 15+ or Swift 5.10+

Option 1: Run with Xcode

1. Open [mac/BridgeMac/Package.swift](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/mac/BridgeMac/Package.swift) in Xcode.
2. Let Xcode resolve the package.
3. Choose the `BridgeMac` executable target.
4. Run the app.

Option 2: Run from Terminal

1. Open a terminal in [mac/BridgeMac](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/mac/BridgeMac).
2. Run:

```bash
swift run BridgeMac
```

Inside the Mac app:

1. Enter the Windows host IP.
2. Leave the default port `5055` unless you changed it on Windows.
3. Enter the same shared secret from `appsettings.json`.
4. Click `Connect`.
5. If you want remote mouse and keyboard sharing, enable `Input Bridge Mode` and approve the permission prompt.

## Input Bridge Mode

The Mac app acts as the controller. When `Input Bridge Mode` is enabled, the app arms itself and waits for the Mac cursor to touch the right edge of the current screen. At that point it starts forwarding input directly to Windows over the local bridge connection.

Current behavior:

- forwards relative mouse movement
- forwards left, right, and middle mouse clicks
- forwards scroll events
- forwards common keyboard keys and modifiers
- reserves `Ctrl + Option + Command + Esc` as a local escape hotkey to return control to the Mac
- injects events on Windows with the native `SendInput` API

The first version is intentionally conservative:

- the transport is local-only and direct to the Windows host
- the feature requires explicit opt-in inside the Mac UI
- the Windows host tears down held keys and mouse buttons when the input session ends
- some uncommon keys may not map perfectly yet

### macOS Accessibility Permission

Input capture on macOS requires Accessibility access. The app prompts for it the first time you enable `Input Bridge Mode`.

If macOS does not show the prompt, open:

- `System Settings > Privacy & Security > Accessibility`

Then enable access for the app or the built `BridgeMac` executable and relaunch if needed.

More detail is in [docs/input-bridge.md](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/docs/input-bridge.md).

## Main Files

- [windows/BridgeWindowsHost/Program.cs](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/windows/BridgeWindowsHost/Program.cs): HTTP and WebSocket routes
- [windows/BridgeWindowsHost/Services/SystemStatusService.cs](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/windows/BridgeWindowsHost/Services/SystemStatusService.cs): Windows metrics collection
- [windows/BridgeWindowsHost/Services/CommandRunnerService.cs](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/windows/BridgeWindowsHost/Services/CommandRunnerService.cs): preview + guarded command execution
- [windows/BridgeWindowsHost/Services/InputInjectionService.cs](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/windows/BridgeWindowsHost/Services/InputInjectionService.cs): native Windows mouse and keyboard injection
- [mac/BridgeMac/Sources/BridgeMacApp/ViewModels/BridgeViewModel.swift](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/mac/BridgeMac/Sources/BridgeMacApp/ViewModels/BridgeViewModel.swift): app state and sync logic
- [mac/BridgeMac/Sources/BridgeMacApp/Views/ContentView.swift](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/mac/BridgeMac/Sources/BridgeMacApp/Views/ContentView.swift): SwiftUI dashboard
- [mac/BridgeMac/Sources/BridgeMacApp/Services/InputBridgeManager.swift](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/mac/BridgeMac/Sources/BridgeMacApp/Services/InputBridgeManager.swift): edge activation, capture, and escape flow
- [docs/local-protocol.md](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/docs/local-protocol.md): endpoint and event reference
- [docs/input-bridge.md](/C:/Users/CHRIS/Documents/Codex/2026-04-26-build-the-first-version-of-a/docs/input-bridge.md): Input Bridge safety and permissions

## Notes And Limitations

- Clipboard sync is text-only in v1.
- Files are transferred through the Windows shared folder configured in `StorageRoot`.
- GPU usage is optional; the current host reports GPU name and memory when available.
- Input Bridge is a safe first version: mouse and common keyboard forwarding are in, but not every macOS key has a perfect Windows mapping yet.
- The Windows host is meant for trusted home or office LANs, not internet exposure.
- The command safety layer is intentionally simple and should be expanded before any broader rollout.

## Suggested Next Steps

- Add a device-pairing screen with QR code or local discovery
- Add richer GPU telemetry and multiple disk support
- Add drag-and-drop file upload in the Mac UI
- Add a Windows tray app or desktop window for local approvals
