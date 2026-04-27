# Input Bridge Modes

This project now supports two local-only keyboard and mouse sharing directions. The main/default setup is `Control Mac from Windows`, and the older Mac-to-Windows path remains available as an optional reverse mode.

## Primary Mode: Control Mac from Windows

When `Control Mac from Windows` is enabled in the Mac app:

- Windows remains the active desktop until its cursor reaches the configured activation edge
- Windows captures mouse movement, clicks, scroll, and common keyboard events
- Windows forwards those events directly over the existing bridge connection
- the Mac injects them with native macOS APIs
- `Ctrl + Alt + Windows + Esc` returns control back to Windows

The Windows desktop app controls which edge is used. The available physical layout options are:

- Left of Windows monitor
- Right of Windows monitor
- Above Windows monitor
- Below Windows monitor

The default is `Left of Windows monitor`. That setting is saved locally on Windows, shown with a small preview in the desktop UI, and used for both activation and cursor return when control comes back to Windows.

Why this is the default mode:

- Windows acts as the main controller
- it keeps the existing LAN-only architecture
- it avoids external KVM apps such as Barrier or Synergy
- it reuses the existing main bridge WebSocket instead of adding a cloud relay

## Reverse Mode: Control Windows from Mac

The original reverse input bridge is still included for setups that also want Mac-to-Windows control.

When `Input Bridge Mode` is enabled:

- the Mac stays local until its cursor reaches the right edge of the current screen
- the Mac captures mouse movement, clicks, scroll, and common keyboard events
- the Mac forwards those events over the dedicated `/ws/input` socket
- the Windows host injects them with the native `SendInput` API
- `Ctrl + Option + Command + Esc` returns control back to the Mac

## Why macOS Accessibility Permission Is Needed

macOS treats both global input capture and synthetic input injection as protected capabilities. The Mac app needs Accessibility access for two separate reasons:

- `Control Mac from Windows`: to inject remote mouse and keyboard events coming from Windows
- `Input Bridge Mode`: to monitor and suppress local keyboard and mouse events while controlling Windows

## Granting Access

The app requests permission the first time you enable either mode that needs it.

If the prompt does not appear or you want to verify it manually:

1. Open `System Settings`.
2. Go to `Privacy & Security`.
3. Open `Accessibility`.
4. Turn on access for the app or the built `BridgeMac` executable.
5. Relaunch the app if macOS does not apply the change immediately.

## Safety Notes

This first version is deliberately conservative:

- it stays on the local network and talks directly to the Windows host
- it requires explicit UI confirmation before either control mode is enabled
- `Control Mac from Windows` keeps Windows local until the configured edge is reached
- when Windows regains control, the cursor is restored just inside that same edge
- the Mac releases held input state when a Windows-to-Mac control session ends
- the Windows host releases held input state when a Mac-to-Windows control session ends
- each direction has a dedicated escape hotkey to return control to the local device
- there is no cloud relay, pairing service, or paid dependency

## Current Limits

- keyboard forwarding focuses on common keys, modifiers, arrows, and function keys
- unusual layouts and some less common keys may not map perfectly yet
- the Windows-primary path currently depends on the Mac app staying connected to the main bridge WebSocket
- the reverse Mac-to-Windows path still activates from the Mac's right edge only in this version
