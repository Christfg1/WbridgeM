import AppKit
import ApplicationServices
import Foundation

final class InputBridgeManager {
    var onPhaseChange: ((InputBridgePhase) -> Void)?
    var onLog: ((String) -> Void)?
    var onError: ((String) -> Void)?

    var currentPhase: InputBridgePhase { phase }
    var isEnabled: Bool { enabled }

    private let inputSocket = WebSocketService()
    private var settings: BridgeConnectionSettings?
    private var enabled = false
    private var phase: InputBridgePhase = .off {
        didSet {
            guard phase != oldValue else { return }
            DispatchQueue.main.async { [phase] in
                self.onPhaseChange?(phase)
            }
        }
    }

    private var edgeTimer: Timer?
    private var eventTap: CFMachPort?
    private var eventTapSource: CFRunLoopSource?
    private var cursorHidden = false
    private var reactivationPauseUntil = Date.distantPast
    private var activationScreen: NSScreen?

    func enable(settings: BridgeConnectionSettings) throws {
        guard !enabled else { return }

        guard Self.requestAccessibilityPermissionIfNeeded() else {
            throw BridgeClientError.permissionRequired(
                "Input Bridge needs macOS Accessibility access before it can capture mouse and keyboard events. Grant access in System Settings > Privacy & Security > Accessibility, then enable Input Bridge again."
            )
        }

        self.settings = settings
        enabled = true
        reactivationPauseUntil = .distantPast

        startEdgeMonitor()
        phase = .armed
        emitLog("Input Bridge armed. Move the Mac cursor to the right edge to take control of Windows.")
    }

    func disable(reason: String) {
        guard enabled || phase != .off else { return }

        stopEventTap()
        revealCursorIfNeeded()
        stopEdgeMonitor()
        activationScreen = nil
        settings = nil
        enabled = false
        phase = .off
        inputSocket.disconnect()
        emitLog(reason)
    }

    func exitToMac(reason: String) {
        guard enabled else { return }

        stopEventTap()
        revealCursorIfNeeded()
        restoreCursorInsideScreenEdge()
        activationScreen = nil
        reactivationPauseUntil = Date().addingTimeInterval(0.6)
        phase = .armed
        inputSocket.disconnect()
        emitLog(reason)
    }

    private func startEdgeMonitor() {
        guard edgeTimer == nil else { return }

        edgeTimer = Timer.scheduledTimer(withTimeInterval: 0.05, repeats: true) { [weak self] _ in
            self?.checkForEdgeActivation()
        }

        if let edgeTimer {
            RunLoop.main.add(edgeTimer, forMode: .common)
        }
    }

    private func stopEdgeMonitor() {
        edgeTimer?.invalidate()
        edgeTimer = nil
    }

    private func checkForEdgeActivation() {
        guard enabled else { return }
        guard phase == .armed else { return }
        guard Date() >= reactivationPauseUntil else { return }
        guard Self.hasAccessibilityPermission() else { return }

        let mouseLocation = NSEvent.mouseLocation
        guard let screen = currentScreen(for: mouseLocation) else { return }
        guard mouseLocation.x >= screen.frame.maxX - 2 else { return }

        activateInputBridge(on: screen)
    }

    private func activateInputBridge(on screen: NSScreen) {
        guard enabled else { return }
        guard phase == .armed else { return }
        guard let settings else { return }

        // Connect only while actively bridging input so closing the socket is enough to release remote state safely.
        do {
            try inputSocket.connect(
                settings: settings,
                path: "/ws/input",
                onDisconnect: { [weak self] message in
                    DispatchQueue.main.async {
                        self?.handleSocketDisconnect(message)
                    }
                }
            )
        } catch {
            disable(reason: "Input Bridge was turned off because the direct input socket could not connect.")
            onError?("Input Bridge could not connect to the Windows input socket: \(error.localizedDescription)")
            return
        }

        guard createEventTap() else {
            inputSocket.disconnect()
            disable(reason: "Input Bridge was turned off because macOS did not allow the event tap to start.")
            onError?("Input Bridge could not start its event tap. Confirm Accessibility access in System Settings > Privacy & Security > Accessibility.")
            return
        }

        activationScreen = screen
        hideCursorIfNeeded()
        phase = .active
        emitLog("Input Bridge active. Press Ctrl + Option + Command + Esc to return control to the Mac.")
    }

    private func createEventTap() -> Bool {
        guard eventTap == nil else { return true }

        let eventTypes: [CGEventType] = [
            .mouseMoved,
            .leftMouseDown,
            .leftMouseUp,
            .leftMouseDragged,
            .rightMouseDown,
            .rightMouseUp,
            .rightMouseDragged,
            .otherMouseDown,
            .otherMouseUp,
            .otherMouseDragged,
            .scrollWheel,
            .keyDown,
            .keyUp,
            .flagsChanged
        ]

        let eventMask = eventTypes.reduce(CGEventMask(0)) { partial, eventType in
            partial | (CGEventMask(1) << eventType.rawValue)
        }

        guard let eventTap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: eventMask,
            callback: Self.eventTapCallback,
            userInfo: UnsafeMutableRawPointer(Unmanaged.passUnretained(self).toOpaque())
        ) else {
            return false
        }

        guard let runLoopSource = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, eventTap, 0) else {
            CFMachPortInvalidate(eventTap)
            return false
        }

        self.eventTap = eventTap
        eventTapSource = runLoopSource
        CFRunLoopAddSource(CFRunLoopGetMain(), runLoopSource, CFRunLoopMode.commonModes)
        CGEvent.tapEnable(tap: eventTap, enable: true)
        return true
    }

    private func stopEventTap() {
        if let eventTapSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), eventTapSource, CFRunLoopMode.commonModes)
        }

        if let eventTap {
            CFMachPortInvalidate(eventTap)
        }

        eventTapSource = nil
        eventTap = nil
    }

    private func handleSocketDisconnect(_ message: String?) {
        guard enabled else { return }

        stopEventTap()
        revealCursorIfNeeded()
        stopEdgeMonitor()
        activationScreen = nil
        settings = nil
        enabled = false
        phase = .off
        inputSocket.disconnect()

        let suffix = message.map { ": \($0)" } ?? ""
        emitLog("Input Bridge disconnected\(suffix)")
        onError?("Input Bridge lost its direct connection to the Windows host\(suffix)")
    }

    private func send(event: InputBridgeEvent) {
        let message = InputBridgeSocketMessage(type: "input-bridge-event", payload: event)
        send(message)
    }

    private func send(_ message: InputBridgeSocketMessage) {
        Task {
            do {
                let payload = try BridgeJSONCoding.encoder.encode(message)
                try await inputSocket.send(payload)
            } catch {
                // The receive loop handles disconnect notifications. Avoid spamming the UI on every missed event.
            }
        }
    }

    private func emitLog(_ message: String) {
        DispatchQueue.main.async {
            self.onLog?(message)
        }
    }

    private func hideCursorIfNeeded() {
        guard !cursorHidden else { return }
        NSCursor.hide()
        cursorHidden = true
    }

    private func revealCursorIfNeeded() {
        guard cursorHidden else { return }
        NSCursor.unhide()
        cursorHidden = false
    }

    private func restoreCursorInsideScreenEdge() {
        guard let screen = activationScreen ?? currentScreen(for: NSEvent.mouseLocation) else { return }

        let currentPoint = NSEvent.mouseLocation
        let y = min(max(currentPoint.y, screen.frame.minY + 8), screen.frame.maxY - 8)
        let point = CGPoint(x: screen.frame.maxX - 8, y: y)
        CGWarpMouseCursorPosition(point)
    }

    private func currentScreen(for point: CGPoint) -> NSScreen? {
        NSScreen.screens.first(where: { $0.frame.contains(point) }) ?? NSScreen.main
    }

    private func handleTapEvent(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let eventTap {
                CGEvent.tapEnable(tap: eventTap, enable: true)
            }

            return Unmanaged.passUnretained(event)
        }

        guard phase == .active else {
            return Unmanaged.passUnretained(event)
        }

        if Self.isEscapeHotkey(event) {
            // This key chord is always kept local so the user has a predictable way back to the Mac.
            DispatchQueue.main.async {
                self.exitToMac(reason: "Returned control to the Mac with the escape hotkey.")
            }

            return nil
        }

        switch type {
        case .mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged:
            let deltaX = Int(event.getIntegerValueField(.mouseEventDeltaX))
            let deltaY = Int(event.getIntegerValueField(.mouseEventDeltaY))
            if deltaX != 0 || deltaY != 0 {
                send(event: InputBridgeEvent(kind: .mouseMove, deltaX: deltaX, deltaY: deltaY))
            }
            return nil

        case .leftMouseDown:
            send(event: InputBridgeEvent(kind: .mouseButton, button: .left, isDown: true))
            return nil

        case .leftMouseUp:
            send(event: InputBridgeEvent(kind: .mouseButton, button: .left, isDown: false))
            return nil

        case .rightMouseDown:
            send(event: InputBridgeEvent(kind: .mouseButton, button: .right, isDown: true))
            return nil

        case .rightMouseUp:
            send(event: InputBridgeEvent(kind: .mouseButton, button: .right, isDown: false))
            return nil

        case .otherMouseDown:
            if event.getIntegerValueField(.mouseEventButtonNumber) == 2 {
                send(event: InputBridgeEvent(kind: .mouseButton, button: .middle, isDown: true))
            }
            return nil

        case .otherMouseUp:
            if event.getIntegerValueField(.mouseEventButtonNumber) == 2 {
                send(event: InputBridgeEvent(kind: .mouseButton, button: .middle, isDown: false))
            }
            return nil

        case .scrollWheel:
            let scrollY = Int(event.getIntegerValueField(.scrollWheelEventDeltaAxis1))
            let scrollX = Int(event.getIntegerValueField(.scrollWheelEventDeltaAxis2))
            if scrollX != 0 || scrollY != 0 {
                send(event: InputBridgeEvent(kind: .scroll, scrollX: scrollX, scrollY: scrollY))
            }
            return nil

        case .keyDown:
            let keyCode = CGKeyCode(event.getIntegerValueField(.keyboardEventKeycode))
            if let virtualKey = WindowsVirtualKeyMapper.virtualKey(for: keyCode) {
                send(event: InputBridgeEvent(kind: .key, isDown: true, windowsVirtualKey: virtualKey))
            }
            return nil

        case .keyUp:
            let keyCode = CGKeyCode(event.getIntegerValueField(.keyboardEventKeycode))
            if let virtualKey = WindowsVirtualKeyMapper.virtualKey(for: keyCode) {
                send(event: InputBridgeEvent(kind: .key, isDown: false, windowsVirtualKey: virtualKey))
            }
            return nil

        case .flagsChanged:
            let keyCode = CGKeyCode(event.getIntegerValueField(.keyboardEventKeycode))
            if
                let virtualKey = WindowsVirtualKeyMapper.modifierVirtualKey(for: keyCode),
                let isDown = WindowsVirtualKeyMapper.modifierIsDown(for: keyCode, flags: event.flags)
            {
                send(event: InputBridgeEvent(kind: .key, isDown: isDown, windowsVirtualKey: virtualKey))
            }
            return nil

        default:
            return nil
        }
    }

    private static func hasAccessibilityPermission() -> Bool {
        AXIsProcessTrusted()
    }

    private static func requestAccessibilityPermissionIfNeeded() -> Bool {
        if hasAccessibilityPermission() {
            return true
        }

        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        return AXIsProcessTrustedWithOptions(options)
    }

    private static func isEscapeHotkey(_ event: CGEvent) -> Bool {
        guard event.type == .keyDown else { return false }

        let flags = event.flags
        let keyCode = CGKeyCode(event.getIntegerValueField(.keyboardEventKeycode))
        return keyCode == 53
            && flags.contains(.maskControl)
            && flags.contains(.maskAlternate)
            && flags.contains(.maskCommand)
    }

    private static let eventTapCallback: CGEventTapCallBack = { _, type, event, userInfo in
        guard let userInfo else {
            return Unmanaged.passUnretained(event)
        }

        let manager = Unmanaged<InputBridgeManager>.fromOpaque(userInfo).takeUnretainedValue()
        return manager.handleTapEvent(type: type, event: event)
    }
}
