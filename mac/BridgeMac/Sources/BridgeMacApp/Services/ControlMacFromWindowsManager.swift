import ApplicationServices
import Foundation

final class ControlMacFromWindowsManager {
    var onStateChange: ((ControlMacFromWindowsState) -> Void)?
    var onLog: ((String) -> Void)?
    var onError: ((String) -> Void)?

    private let apiClient = BridgeAPIClient()
    private let injectionService = MacInputInjectionService()

    private(set) var currentState = ControlMacFromWindowsState(
        enabled: false,
        phase: .off,
        activationEdge: "Left",
        escapeHotkey: "Ctrl + Alt + Windows + Esc",
        requiresMacAccessibilityPermission: true
    )

    func refresh(settings: BridgeConnectionSettings) async throws {
        let state = try await apiClient.fetchControlMacFromWindowsState(settings: settings)
        applyRemoteState(state)
    }

    func enable(settings: BridgeConnectionSettings) async throws {
        guard Self.requestAccessibilityPermissionIfNeeded() else {
            throw BridgeClientError.permissionRequired(
                "Control Mac from Windows needs macOS Accessibility access so it can inject mouse and keyboard events. Grant access in System Settings > Privacy & Security > Accessibility, then enable the mode again."
            )
        }

        let state = try await apiClient.setControlMacFromWindows(enabled: true, settings: settings)
        applyRemoteState(state)
        emitLog("Windows can now take control of the Mac when its cursor reaches the \(state.activationEdge.lowercased()) screen edge.")
    }

    func disable(settings: BridgeConnectionSettings) async throws {
        let state = try await apiClient.setControlMacFromWindows(enabled: false, settings: settings)
        applyRemoteState(state)
        emitLog("Control Mac from Windows turned off.")
    }

    func applyRemoteState(_ state: ControlMacFromWindowsState) {
        let previousPhase = currentState.phase
        currentState = state

        if previousPhase != .active && state.phase == .active {
            injectionService.beginSession()
            emitLog("Windows is now controlling the Mac. Use \(state.escapeHotkey) on Windows to return control there.")
        } else if previousPhase == .active && state.phase != .active {
            injectionService.endSession()
            emitLog("Windows returned control to Windows.")
        } else if state.phase != .active {
            injectionService.endSession()
        }

        DispatchQueue.main.async {
            self.onStateChange?(state)
        }
    }

    func handleRemoteInput(_ inputEvent: InputBridgeEvent) {
        guard currentState.enabled else { return }
        guard currentState.phase == .active else { return }
        injectionService.inject(inputEvent)
    }

    func reset(reason: String) {
        currentState = ControlMacFromWindowsState(
            enabled: false,
            phase: .off,
            activationEdge: currentState.activationEdge,
            escapeHotkey: currentState.escapeHotkey,
            requiresMacAccessibilityPermission: currentState.requiresMacAccessibilityPermission
        )

        injectionService.endSession()
        DispatchQueue.main.async {
            self.onStateChange?(self.currentState)
        }
        emitLog(reason)
    }

    private func emitLog(_ message: String) {
        DispatchQueue.main.async {
            self.onLog?(message)
        }
    }

    private static func requestAccessibilityPermissionIfNeeded() -> Bool {
        if AXIsProcessTrusted() {
            return true
        }

        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        return AXIsProcessTrustedWithOptions(options)
    }
}
