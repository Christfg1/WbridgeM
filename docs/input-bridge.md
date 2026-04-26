# Input Bridge Mode

Input Bridge Mode is the built-in keyboard and mouse sharing path for this project. It is meant to cover the first local-only version of Mac-to-Windows control without Barrier, Synergy, or another external KVM tool.

## What It Does

When the mode is enabled in the Mac app:

- the Mac stays in control until the cursor reaches the right edge of the current screen
- the Mac app then starts forwarding mouse movement, clicks, scroll, and common keyboard events to Windows
- the Windows host injects those events with the native `SendInput` API
- pressing `Ctrl + Option + Command + Esc` returns control to the Mac

## Why Accessibility Permission Is Needed

macOS treats global keyboard and mouse monitoring as a protected capability. The Mac app needs Accessibility access so it can:

- detect mouse and keyboard events outside its own window
- suppress local events while Windows is being controlled
- reserve the escape hotkey to return control to the Mac

## Granting Access

The app requests permission the first time you enable `Input Bridge Mode`.

If the prompt does not appear or you want to confirm it manually:

1. Open `System Settings`.
2. Go to `Privacy & Security`.
3. Open `Accessibility`.
4. Turn on access for the app or the built `BridgeMac` executable.
5. Relaunch the app if macOS does not apply the change immediately.

## Safety Notes

This first version is deliberately conservative:

- it stays on the local network and talks directly to the Windows host
- it requires explicit UI confirmation before enabling
- it connects the input socket only while remote control is active
- the Windows host releases held keys and mouse buttons when the input session ends
- the escape hotkey is always handled locally on the Mac

## Current Limits

- keyboard forwarding currently focuses on common keys, arrows, modifiers, and function keys
- unusual layouts and some less common keys may not map perfectly yet
- activation is right-edge only in v1
- there is no cloud relay, pairing service, or remote internet access path
