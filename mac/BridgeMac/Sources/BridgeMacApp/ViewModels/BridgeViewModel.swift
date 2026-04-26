import AppKit
import Foundation
import SwiftUI

@MainActor
final class BridgeViewModel: ObservableObject {
    enum ConnectionState: String {
        case disconnected
        case connecting
        case connected

        var label: String {
            switch self {
            case .disconnected:
                return "Disconnected"
            case .connecting:
                return "Connecting"
            case .connected:
                return "Connected"
            }
        }
    }

    @Published var settings: BridgeConnectionSettings {
        didSet { settingsStore.save(settings) }
    }

    @Published var connectionState: ConnectionState = .disconnected
    @Published var bridgeState: BridgeState?
    @Published var status: StatusSnapshot?
    @Published var remoteClipboardText: String = ""
    @Published var localClipboardText: String
    @Published var remoteFiles: [FileEntry] = []
    @Published var commandText: String = "Get-ChildItem $env:USERPROFILE\\Documents"
    @Published var commandShell: CommandShell = .powerShell
    @Published var commandPreview: CommandPreviewResponse?
    @Published var commandResult: RunCommandResponse?
    @Published var controlMacFromWindowsPhase: InputBridgePhase = .off
    @Published var isControlMacFromWindowsEnabled = false
    @Published var controlMacActivationEdge = "Right"
    @Published var controlMacEscapeHotkey = "Ctrl + Alt + Windows + Esc"
    @Published var inputBridgePhase: InputBridgePhase = .off
    @Published var isInputBridgeModeEnabled = false
    @Published var errorMessage: String?
    @Published var activityLog: [String] = []
    @Published var isRunningCommand = false
    @Published var isRefreshingFiles = false

    private let apiClient = BridgeAPIClient()
    private let webSocketService = WebSocketService()
    private let clipboardMonitor = ClipboardMonitor()
    private let settingsStore = SettingsStore()
    private let controlMacFromWindowsManager = ControlMacFromWindowsManager()
    private let inputBridgeManager = InputBridgeManager()
    private var statusPollingTask: Task<Void, Never>?
    private var isApplyingRemoteClipboard = false
    private var lastRemoteClipboardText: String = ""

    init() {
        let savedSettings = settingsStore.load()
        settings = savedSettings
        localClipboardText = clipboardMonitor.currentText()

        clipboardMonitor.start { [weak self] text in
            Task { @MainActor [weak self] in
                await self?.handleLocalClipboardChange(text)
            }
        }

        inputBridgeManager.onPhaseChange = { [weak self] phase in
            Task { @MainActor [weak self] in
                self?.handleInputBridgePhaseChange(phase)
            }
        }

        inputBridgeManager.onLog = { [weak self] message in
            Task { @MainActor [weak self] in
                self?.appendLog(message)
            }
        }

        inputBridgeManager.onError = { [weak self] message in
            Task { @MainActor [weak self] in
                self?.errorMessage = message
            }
        }

        controlMacFromWindowsManager.onStateChange = { [weak self] state in
            Task { @MainActor [weak self] in
                self?.syncControlMacFromWindowsState(state)
            }
        }

        controlMacFromWindowsManager.onLog = { [weak self] message in
            Task { @MainActor [weak self] in
                self?.appendLog(message)
            }
        }

        controlMacFromWindowsManager.onError = { [weak self] message in
            Task { @MainActor [weak self] in
                self?.errorMessage = message
            }
        }
    }

    var canConnect: Bool {
        !settings.host.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && !settings.sharedSecret.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    var commandPreviewSummary: String {
        guard let commandPreview else {
            return "Preview the command to see warnings before you run it."
        }

        if commandPreview.blocked {
            return commandPreview.blockedReason ?? "This command is blocked."
        }

        if commandPreview.warnings.isEmpty {
            return "No warnings. This command is ready to run."
        }

        return commandPreview.warnings.joined(separator: "\n")
    }

    var inputBridgeStatusSummary: String {
        switch inputBridgePhase {
        case .off:
            return "Off. Input stays on the Mac."
        case .armed:
            return "Armed. Move the Mac cursor to the right edge to begin controlling Windows."
        case .active:
            return "Active. Mouse, scroll, and common keyboard keys are being forwarded to Windows."
        }
    }

    var controlMacFromWindowsSummary: String {
        switch controlMacFromWindowsPhase {
        case .off:
            return "Off. Windows will not capture and forward input to the Mac."
        case .armed:
            return "Armed. When Windows reaches the right screen edge, it can begin controlling the Mac."
        case .active:
            return "Active. Windows is currently driving the Mac with remote mouse and keyboard input."
        }
    }

    func connect() async {
        guard canConnect else {
            errorMessage = "Enter the Windows host and shared secret before connecting."
            return
        }

        connectionState = .connecting
        appendLog("Connecting to \(settings.host):\(settings.port)")

        do {
            bridgeState = try await apiClient.fetchBridgeState(settings: settings)

            async let statusRequest = apiClient.fetchStatus(settings: settings)
            async let clipboardRequest = apiClient.fetchClipboard(settings: settings)
            async let fileRequest = apiClient.listFiles(settings: settings)
            async let controlMacStateRequest = controlMacFromWindowsManager.refresh(settings: settings)

            status = try await statusRequest
            let clipboard = try await clipboardRequest
            let fileList = try await fileRequest
            remoteFiles = fileList.entries
            try await controlMacStateRequest

            applyRemoteClipboard(clipboard, mirrorToMac: settings.autoSyncClipboard)
            try webSocketService.connect(
                settings: settings,
                onEvent: { [weak self] data in
                    Task { @MainActor [weak self] in
                        await self?.handleSocketMessage(data)
                    }
                },
                onDisconnect: { [weak self] message in
                    Task { @MainActor [weak self] in
                        self?.handleSocketDisconnect(message)
                    }
                }
            )

            connectionState = .connected
            startStatusPolling()
            appendLog("Connected to \(bridgeState?.hostName ?? settings.host)")
        } catch {
            connectionState = .disconnected
            errorMessage = error.localizedDescription
            appendLog("Connection failed: \(error.localizedDescription)")
        }
    }

    func disconnect() {
        if isControlMacFromWindowsEnabled {
            controlMacFromWindowsManager.reset(reason: "Control Mac from Windows turned off because the bridge disconnected.")
        }

        if isInputBridgeModeEnabled {
            disableInputBridge(reason: "Input Bridge was turned off because the bridge disconnected.")
        }

        statusPollingTask?.cancel()
        statusPollingTask = nil
        webSocketService.disconnect()
        connectionState = .disconnected
        appendLog("Disconnected from the Windows bridge")
    }

    func refreshStatus() async {
        guard connectionState == .connected else { return }

        do {
            status = try await apiClient.fetchStatus(settings: settings)
            appendLog("Refreshed Windows status")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func refreshFiles() async {
        guard connectionState == .connected else { return }
        isRefreshingFiles = true
        defer { isRefreshingFiles = false }

        do {
            let fileList = try await apiClient.listFiles(settings: settings)
            remoteFiles = fileList.entries
            appendLog("Refreshed remote file list")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func pullClipboardFromWindows() async {
        guard connectionState == .connected else { return }

        do {
            let snapshot = try await apiClient.fetchClipboard(settings: settings)
            applyRemoteClipboard(snapshot, mirrorToMac: true)
            appendLog("Pulled Windows clipboard to the Mac")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func pushClipboardToWindows() async {
        guard connectionState == .connected else { return }

        do {
            let snapshot = try await apiClient.setClipboard(text: localClipboardText, sourceDevice: "mac", settings: settings)
            remoteClipboardText = snapshot.text
            lastRemoteClipboardText = snapshot.text
            appendLog("Sent Mac clipboard to Windows")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func uploadFile(from fileURL: URL) async {
        guard connectionState == .connected else { return }

        let hadAccess = fileURL.startAccessingSecurityScopedResource()
        defer {
            if hadAccess {
                fileURL.stopAccessingSecurityScopedResource()
            }
        }

        do {
            let uploaded = try await apiClient.uploadFile(fileURL: fileURL, subdirectory: nil, settings: settings)
            appendLog("Uploaded \(uploaded.relativePath)")
            await refreshFiles()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func downloadFile(_ entry: FileEntry) async {
        guard connectionState == .connected else { return }
        guard !entry.isDirectory else { return }

        do {
            let fileData = try await apiClient.downloadFile(relativePath: entry.relativePath, settings: settings)
            let savePanel = NSSavePanel()
            savePanel.canCreateDirectories = true
            savePanel.nameFieldStringValue = entry.fileName

            guard savePanel.runModal() == .OK, let destinationURL = savePanel.url else {
                return
            }

            try fileData.write(to: destinationURL)
            appendLog("Downloaded \(entry.relativePath)")
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func previewCurrentCommand() async {
        guard connectionState == .connected else { return }

        do {
            commandPreview = try await apiClient.previewCommand(command: commandText, shell: commandShell, settings: settings)
            if let commandPreview, commandPreview.blocked {
                appendLog("Blocked command preview: \(commandPreview.blockedReason ?? "unknown rule")")
            } else {
                appendLog("Previewed command: \(commandText)")
            }
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func runCurrentCommand() async {
        guard connectionState == .connected else { return }
        isRunningCommand = true
        defer { isRunningCommand = false }

        do {
            commandResult = try await apiClient.runCommand(command: commandText, shell: commandShell, settings: settings)
            appendLog("Command finished with exit code \(commandResult?.exitCode ?? -1)")
        } catch {
            errorMessage = error.localizedDescription
            appendLog("Command failed: \(error.localizedDescription)")
        }
    }

    func confirmEnableInputBridge() {
        guard connectionState == .connected else {
            errorMessage = "Connect to the Windows bridge before enabling Input Bridge Mode."
            isInputBridgeModeEnabled = false
            return
        }

        do {
            try inputBridgeManager.enable(settings: settings)
            isInputBridgeModeEnabled = true
            inputBridgePhase = inputBridgeManager.currentPhase
        } catch {
            isInputBridgeModeEnabled = false
            errorMessage = error.localizedDescription
        }
    }

    func disableInputBridge(reason: String = "Input Bridge mode turned off.") {
        inputBridgeManager.disable(reason: reason)
        isInputBridgeModeEnabled = false
        inputBridgePhase = .off
    }

    func returnInputBridgeControlToMac() {
        guard inputBridgePhase == .active else { return }
        inputBridgeManager.exitToMac(reason: "Returned control to the Mac from the Input Bridge controls.")
    }

    func enableControlMacFromWindows() async {
        guard connectionState == .connected else {
            errorMessage = "Connect to the Windows bridge before enabling Control Mac from Windows."
            isControlMacFromWindowsEnabled = false
            return
        }

        do {
            try await controlMacFromWindowsManager.enable(settings: settings)
        } catch {
            isControlMacFromWindowsEnabled = false
            errorMessage = error.localizedDescription
        }
    }

    func disableControlMacFromWindows() async {
        guard connectionState == .connected else {
            controlMacFromWindowsManager.reset(reason: "Control Mac from Windows turned off.")
            return
        }

        do {
            try await controlMacFromWindowsManager.disable(settings: settings)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func startStatusPolling() {
        statusPollingTask?.cancel()
        statusPollingTask = Task { [weak self] in
            while let self, !Task.isCancelled {
                try? await Task.sleep(for: .seconds(8))
                guard !Task.isCancelled else { break }

                do {
                    self.status = try await self.apiClient.fetchStatus(settings: self.settings)
                } catch {
                    self.appendLog("Background status refresh failed: \(error.localizedDescription)")
                }
            }
        }
    }

    private func handleLocalClipboardChange(_ text: String) async {
        localClipboardText = text

        guard settings.autoSyncClipboard else { return }
        guard connectionState == .connected else { return }
        // Ignore clipboard writes that we just applied from the Windows side to avoid sync loops.
        guard !isApplyingRemoteClipboard else { return }
        guard text != lastRemoteClipboardText else { return }

        do {
            let snapshot = try await apiClient.setClipboard(text: text, sourceDevice: "mac", settings: settings)
            remoteClipboardText = snapshot.text
            lastRemoteClipboardText = snapshot.text
            appendLog("Auto-synced Mac clipboard to Windows")
        } catch {
            appendLog("Clipboard sync failed: \(error.localizedDescription)")
        }
    }

    private func applyRemoteClipboard(_ snapshot: ClipboardContent, mirrorToMac: Bool) {
        remoteClipboardText = snapshot.text
        lastRemoteClipboardText = snapshot.text

        guard mirrorToMac else { return }
        guard localClipboardText != snapshot.text else { return }

        isApplyingRemoteClipboard = true
        clipboardMonitor.setClipboardText(snapshot.text)
        localClipboardText = snapshot.text

        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) { [weak self] in
            self?.isApplyingRemoteClipboard = false
        }
    }

    private func handleSocketMessage(_ data: Data) async {
        do {
            let probe = try BridgeJSONCoding.decoder.decode(EventTypeProbe.self, from: data)

            // Decode the envelope twice: once to identify the event type, then again with the correct payload model.
            switch probe.type {
            case "status-updated":
                let event = try BridgeJSONCoding.decoder.decode(BridgeEventEnvelope<StatusSnapshot>.self, from: data)
                status = event.payload
            case "clipboard-updated":
                let event = try BridgeJSONCoding.decoder.decode(BridgeEventEnvelope<ClipboardContent>.self, from: data)
                applyRemoteClipboard(event.payload, mirrorToMac: settings.autoSyncClipboard)
                appendLog("Received a Windows clipboard update")
            case "command-completed":
                let event = try BridgeJSONCoding.decoder.decode(BridgeEventEnvelope<RunCommandResponse>.self, from: data)
                commandResult = event.payload
                appendLog("Received a command completion update")
            case "control-mac-state":
                let event = try BridgeJSONCoding.decoder.decode(BridgeEventEnvelope<ControlMacFromWindowsState>.self, from: data)
                controlMacFromWindowsManager.applyRemoteState(event.payload)
            case "control-mac-input":
                let event = try BridgeJSONCoding.decoder.decode(BridgeEventEnvelope<InputBridgeEvent>.self, from: data)
                controlMacFromWindowsManager.handleRemoteInput(event.payload)
            default:
                appendLog("Received bridge event: \(probe.type)")
            }
        } catch {
            appendLog("Failed to decode a WebSocket event: \(error.localizedDescription)")
        }
    }

    private func handleSocketDisconnect(_ message: String?) {
        if connectionState == .connected {
            if isControlMacFromWindowsEnabled {
                controlMacFromWindowsManager.reset(reason: "Control Mac from Windows turned off because the main bridge socket disconnected.")
            }

            if isInputBridgeModeEnabled {
                disableInputBridge(reason: "Input Bridge was turned off because the main bridge socket disconnected.")
            }

            statusPollingTask?.cancel()
            statusPollingTask = nil
            connectionState = .disconnected
            appendLog("WebSocket disconnected\(message.map { ": \($0)" } ?? "")")
        }
    }

    private func handleInputBridgePhaseChange(_ phase: InputBridgePhase) {
        inputBridgePhase = phase
        isInputBridgeModeEnabled = phase != .off
    }

    private func syncControlMacFromWindowsState(_ state: ControlMacFromWindowsState) {
        isControlMacFromWindowsEnabled = state.enabled
        controlMacFromWindowsPhase = state.phase
        controlMacActivationEdge = state.activationEdge
        controlMacEscapeHotkey = state.escapeHotkey
    }

    private func appendLog(_ message: String) {
        let timestamp = DateFormatter.bridgeLogTimestamp.string(from: Date())
        activityLog.insert("[\(timestamp)] \(message)", at: 0)
        activityLog = Array(activityLog.prefix(30))
    }
}

private extension DateFormatter {
    static let bridgeLogTimestamp: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm:ss"
        return formatter
    }()
}
