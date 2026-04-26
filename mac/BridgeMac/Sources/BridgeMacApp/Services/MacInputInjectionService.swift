import AppKit
import ApplicationServices
import Foundation

final class MacInputInjectionService {
    private var pressedButtons: Set<InputBridgeMouseButton> = []
    private var pressedKeyCodes: Set<CGKeyCode> = []

    func beginSession() {
        releaseAllState()
    }

    func endSession() {
        releaseAllState()
    }

    func inject(_ inputEvent: InputBridgeEvent) {
        switch inputEvent.kind {
        case .mouseMove:
            injectMouseMove(inputEvent)
        case .mouseButton:
            injectMouseButton(inputEvent)
        case .scroll:
            injectScroll(inputEvent)
        case .key:
            injectKey(inputEvent)
        }
    }

    private func injectMouseMove(_ inputEvent: InputBridgeEvent) {
        guard inputEvent.deltaX != 0 || inputEvent.deltaY != 0 else { return }

        let currentPoint = NSEvent.mouseLocation
        let newPoint = boundedPoint(
            CGPoint(
                x: currentPoint.x + CGFloat(inputEvent.deltaX),
                y: currentPoint.y + CGFloat(inputEvent.deltaY)
            )
        )

        // Warp first so the synthetic event lands at the same global cursor position the user sees.
        CGWarpMouseCursorPosition(newPoint)

        let eventType: CGEventType
        let mouseButton: CGMouseButton
        if pressedButtons.contains(.left) {
            eventType = .leftMouseDragged
            mouseButton = .left
        } else if pressedButtons.contains(.right) {
            eventType = .rightMouseDragged
            mouseButton = .right
        } else if pressedButtons.contains(.middle) {
            eventType = .otherMouseDragged
            mouseButton = .center
        } else {
            eventType = .mouseMoved
            mouseButton = .left
        }

        if let event = CGEvent(mouseEventSource: nil, mouseType: eventType, mouseCursorPosition: newPoint, mouseButton: mouseButton) {
            event.post(tap: .cghidEventTap)
        }
    }

    private func injectMouseButton(_ inputEvent: InputBridgeEvent) {
        guard let button = inputEvent.button, let isDown = inputEvent.isDown else { return }

        let point = NSEvent.mouseLocation
        let mouseButton = cgMouseButton(for: button)
        let mouseType = mouseEventType(for: button, isDown: isDown)

        if isDown {
            pressedButtons.insert(button)
        }

        if let event = CGEvent(mouseEventSource: nil, mouseType: mouseType, mouseCursorPosition: point, mouseButton: mouseButton) {
            event.post(tap: .cghidEventTap)
        }

        if !isDown {
            pressedButtons.remove(button)
        }
    }

    private func injectScroll(_ inputEvent: InputBridgeEvent) {
        guard inputEvent.scrollX != 0 || inputEvent.scrollY != 0 else { return }

        if let event = CGEvent(
            scrollWheelEvent2Source: nil,
            units: .line,
            wheelCount: 2,
            wheel1: Int32(-inputEvent.scrollY),
            wheel2: Int32(inputEvent.scrollX),
            wheel3: 0
        ) {
            event.post(tap: .cghidEventTap)
        }
    }

    private func injectKey(_ inputEvent: InputBridgeEvent) {
        guard
            let windowsVirtualKey = inputEvent.windowsVirtualKey,
            let isDown = inputEvent.isDown,
            let keyCode = MacVirtualKeyMapper.keyCode(for: windowsVirtualKey)
        else {
            return
        }

        if isDown {
            pressedKeyCodes.insert(keyCode)
        } else {
            pressedKeyCodes.remove(keyCode)
        }

        if let event = CGEvent(keyboardEventSource: nil, virtualKey: keyCode, keyDown: isDown) {
            event.flags = MacVirtualKeyMapper.flags(for: pressedKeyCodes)
            event.post(tap: .cghidEventTap)
        }
    }

    private func releaseAllState() {
        let currentPoint = NSEvent.mouseLocation

        for button in pressedButtons {
            let mouseType = mouseEventType(for: button, isDown: false)
            if let event = CGEvent(mouseEventSource: nil, mouseType: mouseType, mouseCursorPosition: currentPoint, mouseButton: cgMouseButton(for: button)) {
                event.post(tap: .cghidEventTap)
            }
        }

        for keyCode in pressedKeyCodes {
            if let event = CGEvent(keyboardEventSource: nil, virtualKey: keyCode, keyDown: false) {
                event.post(tap: .cghidEventTap)
            }
        }

        pressedButtons.removeAll()
        pressedKeyCodes.removeAll()
    }

    private func boundedPoint(_ point: CGPoint) -> CGPoint {
        let frames = NSScreen.screens.map(\.frame)
        guard let firstFrame = frames.first else { return point }

        let unionFrame = frames.dropFirst().reduce(firstFrame) { partial, frame in
            partial.union(frame)
        }

        let x = min(max(point.x, unionFrame.minX + 1), unionFrame.maxX - 1)
        let y = min(max(point.y, unionFrame.minY + 1), unionFrame.maxY - 1)
        return CGPoint(x: x, y: y)
    }

    private func cgMouseButton(for button: InputBridgeMouseButton) -> CGMouseButton {
        switch button {
        case .left:
            return .left
        case .right:
            return .right
        case .middle:
            return .center
        }
    }

    private func mouseEventType(for button: InputBridgeMouseButton, isDown: Bool) -> CGEventType {
        switch (button, isDown) {
        case (.left, true):
            return .leftMouseDown
        case (.left, false):
            return .leftMouseUp
        case (.right, true):
            return .rightMouseDown
        case (.right, false):
            return .rightMouseUp
        case (.middle, true):
            return .otherMouseDown
        case (.middle, false):
            return .otherMouseUp
        }
    }
}
