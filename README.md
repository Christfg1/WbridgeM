# Mac + Windows Bridge v1

Local-only bridge app for a Mac client and a Windows host on the same LAN.

Version 1 includes:

- Shared clipboard between Mac and Windows
- File transfer into and out of a Windows shared folder
- Windows status dashboard for CPU, RAM, disk, and GPU details when available
- Remote command runner with preview and confirmation
- `Control Mac from Windows` as the primary/default input bridge mode
- `Control Windows from Mac` as an optional reverse input bridge mode
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
    |-- BridgeWindowsDesktop/
    |   |-- BridgeWindowsDesktop.csproj
    |   |-- MainForm.cs
    |   `-- Program.cs
    `-- BridgeWindowsHost/
        |-- BridgeWindowsHost.csproj
        |-- Program.cs
        |-- appsettings.json
        |-- Models/
        `-- Services/
```

## How It Works

The Windows app hosts a small LAN-only API on port `5055` by default. The Mac SwiftUI app connects to that host over HTTP for request/response actions and opens the main WebSocket at `/ws` for live status, clipboard, and Windows-to-Mac control events.

There are now two input bridge directions:

- Primary/default: Windows captures input at its screen edge and forwards it to the Mac over the existing main bridge WebSocket.
- Reverse/optional: the Mac captures input at its screen edge and forwards it to Windows over the dedicated `/ws/input` socket.

The trust model stays simple:

- Both devices must be on the same local network.
- The Mac user enters the Windows IP address manually.
- Every API request includes `X-Bridge-Secret`.
- Commands must be previewed and explicitly confirmed in the Mac UI.
- The Windows host blocks a few destructive command tokens by default.
- Each input bridge direction must be enabled explicitly in the Mac UI.
- No cloud relay, no paid APIs, and no internet service is required.

## Run The Windows Desktop UI

Prerequisites:

- Windows 10 or 11
- .NET 8 SDK

Steps:

1. Open a terminal in `windows/BridgeWindowsDesktop`.
2. Run:

```powershell
dotnet restore
dotnet run
```

3. The desktop app starts the copied `BridgeWindowsHost` build in the background and shows:
   - server running status
   - current IP addresses
   - port
   - masked shared secret status
   - connected Mac client count
   - the `Control Mac from Windows` toggle
   - buttons for the shared folder and bridge start/stop

The desktop UI is the simplest way to supervise the Windows side without relying on a console window.

## Run The Windows Host Directly

Prerequisites:

- Windows 10 or 11
- .NET 8 SDK
- PowerShell available on the machine

Steps:

1. Open `windows/BridgeWindowsHost/appsettings.json` and change `SharedSecret`.
2. Optional: change `Port` or `StorageRoot` if you want a different shared folder.
3. Open a terminal in `windows/BridgeWindowsHost`.
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
- `GET /api/input/control-mac`
- `POST /api/input/control-mac`
- `GET /api/health`
- `GET /ws`
- `GET /ws/input`

## Run The Mac App

Prerequisites:

- macOS 13+
- Xcode 15+ or Swift 5.10+

Option 1: Run with Xcode

1. Open `mac/BridgeMac/Package.swift` in Xcode.
2. Let Xcode resolve the package.
3. Choose the `BridgeMac` executable target.
4. Run the app.

Option 2: Run from Terminal

1. Open a terminal in `mac/BridgeMac`.
2. Run:

```bash
swift run BridgeMac
```

Inside the Mac app:

1. Enter the Windows host IP.
2. Leave the default port `5055` unless you changed it on Windows.
3. Enter the same shared secret from `appsettings.json`.
4. Click `Connect`.
5. Enable `Control Mac from Windows` if you want Windows to be the primary controller.
6. Optionally enable `Input Bridge Mode` if you also want the reverse Mac-to-Windows path available.

## Input Bridge Modes

### Primary: Control Mac from Windows

This is the main/default desktop sharing path for the app.

When `Control Mac from Windows` is enabled:

- Windows stays local until its cursor reaches the right edge of the Windows desktop
- Windows begins forwarding mouse movement, clicks, scroll, and common keyboard events to the Mac
- the Mac injects those events with native macOS APIs
- `Ctrl + Alt + Windows + Esc` returns control back to Windows

This direction uses:

- `GET/POST /api/input/control-mac` for enable/disable state
- the existing `/ws` bridge socket for live `control-mac-state` and `control-mac-input` events

### Reverse: Control Windows from Mac

The older reverse direction is still included as an optional mode.

When `Input Bridge Mode` is enabled:

- the Mac waits for its cursor to reach the right edge of the current screen
- the Mac forwards mouse movement, clicks, scroll, and common keyboard events to Windows
- the Windows host injects those events with the native `SendInput` API
- `Ctrl + Option + Command + Esc` returns control back to the Mac

This direction uses:

- the dedicated `/ws/input` socket
- the existing `InputBridgeManager` and `InputInjectionService` path

## macOS Accessibility Permission

macOS Accessibility access is required for both input bridge directions:

- `Control Mac from Windows` needs it so the Mac can inject remote mouse and keyboard events into macOS.
- `Input Bridge Mode` needs it so the Mac can capture and suppress global input while controlling Windows.

The app prompts for permission when needed. If macOS does not show the prompt, open:

- `System Settings > Privacy & Security > Accessibility`

Then enable access for the app or the built `BridgeMac` executable and relaunch if needed.

More detail is in `docs/input-bridge.md`.

## Main Files

- `windows/BridgeWindowsHost/Program.cs`: HTTP and WebSocket routes
- `windows/BridgeWindowsDesktop/MainForm.cs`: WinForms control window for the Windows bridge
- `windows/BridgeWindowsDesktop/BridgeHostProcessManager.cs`: starts and stops the bundled host process for the desktop UI
- `windows/BridgeWindowsDesktop/BridgeDesktopApiClient.cs`: polls the local host API for status and control state
- `windows/BridgeWindowsHost/Services/ControlMacInputBridgeService.cs`: Windows edge capture and Windows-to-Mac input forwarding
- `windows/BridgeWindowsHost/Services/InputInjectionService.cs`: native Windows mouse and keyboard injection for the reverse path
- `windows/BridgeWindowsHost/Services/SystemStatusService.cs`: Windows metrics collection
- `windows/BridgeWindowsHost/Services/CommandRunnerService.cs`: preview + guarded command execution
- `mac/BridgeMac/Sources/BridgeMacApp/ViewModels/BridgeViewModel.swift`: app state and sync logic for both input directions
- `mac/BridgeMac/Sources/BridgeMacApp/Views/ContentView.swift`: SwiftUI dashboard and mode toggles
- `mac/BridgeMac/Sources/BridgeMacApp/Services/ControlMacFromWindowsManager.swift`: Mac receiver/session manager for the Windows-primary path
- `mac/BridgeMac/Sources/BridgeMacApp/Services/MacInputInjectionService.swift`: native macOS mouse and keyboard injection
- `mac/BridgeMac/Sources/BridgeMacApp/Services/InputBridgeManager.swift`: reverse Mac-to-Windows edge capture and escape flow
- `docs/local-protocol.md`: endpoint and event reference
- `docs/input-bridge.md`: input bridge behavior, safety, and permissions

## Notes And Limitations

- Clipboard sync is text-only in v1.
- Files are transferred through the Windows shared folder configured in `StorageRoot`.
- GPU usage is optional; the current host reports GPU name and memory when available.
- The Windows-primary input path is a safe first version focused on local mouse, scroll, and common keyboard forwarding.
- The reverse Mac-to-Windows path is still available, but some uncommon keys may not map perfectly yet.
- The Windows host is meant for trusted home or office LANs, not internet exposure.
- The command safety layer is intentionally simple and should be expanded before any broader rollout.

## Suggested Next Steps

- Add configurable activation edges for each direction
- Add richer GPU telemetry and multiple disk support
- Add drag-and-drop file upload in the Mac UI
- Add a Windows tray app or local approval window for control-mode changes
