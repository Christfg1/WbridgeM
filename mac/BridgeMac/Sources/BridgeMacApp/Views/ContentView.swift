import SwiftUI
import UniformTypeIdentifiers

struct ContentView: View {
    @StateObject private var viewModel = BridgeViewModel()
    @State private var showingFileImporter = false
    @State private var showingCommandConfirmation = false
    @State private var showingControlMacConsent = false
    @State private var showingInputBridgeConsent = false

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                header
                connectionSection
                statusSection
                controlMacFromWindowsSection
                inputBridgeSection
                clipboardSection
                filesSection
                commandSection
                activitySection
            }
            .padding(24)
        }
        .frame(minWidth: 1080, minHeight: 900)
        .background(Color(nsColor: .windowBackgroundColor))
        .fileImporter(isPresented: $showingFileImporter, allowedContentTypes: [.data]) { result in
            switch result {
            case let .success(fileURL):
                Task {
                    await viewModel.uploadFile(from: fileURL)
                }
            case let .failure(error):
                viewModel.errorMessage = error.localizedDescription
            }
        }
        .alert("Bridge Error", isPresented: Binding(
            get: { viewModel.errorMessage != nil },
            set: { if !$0 { viewModel.errorMessage = nil } }
        )) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(viewModel.errorMessage ?? "")
        }
        .confirmationDialog("Enable Control Mac from Windows?", isPresented: $showingControlMacConsent, titleVisibility: .visible) {
            Button("Enable Control Mac from Windows") {
                Task {
                    await viewModel.enableControlMacFromWindows()
                }
            }

            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This makes Windows the primary controller. When the Windows cursor reaches the configured screen edge, Windows will capture mouse and keyboard input, send it directly over the local bridge, and the Mac will inject it using macOS Accessibility APIs.")
        }
        .confirmationDialog("Enable Input Bridge Mode?", isPresented: $showingInputBridgeConsent, titleVisibility: .visible) {
            Button("Enable Input Bridge Mode") {
                viewModel.confirmEnableInputBridge()
            }

            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This captures global mouse and keyboard events on the Mac, starts forwarding them directly to your Windows machine when the cursor reaches the right edge, and reserves Ctrl + Option + Command + Esc to return control to the Mac. macOS Accessibility permission is required.")
        }
        .confirmationDialog("Run this command on the Windows PC?", isPresented: $showingCommandConfirmation, titleVisibility: .visible) {
            Button("Run Command", role: .destructive) {
                Task {
                    await viewModel.runCurrentCommand()
                }
            }

            Button("Cancel", role: .cancel) {}
        } message: {
            Text(viewModel.commandPreviewSummary)
        }
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Mac + Windows Bridge")
                .font(.system(size: 30, weight: .bold, design: .rounded))

            Text("Local-only clipboard, files, system status, and remote command control for one Windows machine on your LAN.")
                .foregroundStyle(.secondary)
        }
    }

    private var connectionSection: some View {
        SectionCard(title: "Connection") {
            VStack(alignment: .leading, spacing: 16) {
                HStack(spacing: 12) {
                    LabeledField(title: "Windows Host") {
                        TextField("192.168.1.42", text: $viewModel.settings.host)
                            .textFieldStyle(.roundedBorder)
                    }

                    LabeledField(title: "Port") {
                        TextField("5055", text: Binding(
                            get: { String(viewModel.settings.port) },
                            set: { viewModel.settings.port = Int($0) ?? 5055 }
                        ))
                        .frame(width: 90)
                        .textFieldStyle(.roundedBorder)
                    }

                    LabeledField(title: "Shared Secret") {
                        SecureField("change-this-secret", text: $viewModel.settings.sharedSecret)
                            .textFieldStyle(.roundedBorder)
                    }
                }

                Toggle("Auto-apply Windows clipboard to the Mac clipboard", isOn: Binding(
                    get: { viewModel.settings.autoSyncClipboard },
                    set: { newValue in
                        viewModel.settings.autoSyncClipboard = newValue
                        if newValue {
                            Task {
                                await viewModel.pullClipboardFromWindows()
                            }
                        }
                    }
                ))

                HStack(spacing: 12) {
                    Button("Connect") {
                        Task {
                            await viewModel.connect()
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(viewModel.connectionState == .connecting)

                    Button("Disconnect") {
                        viewModel.disconnect()
                    }
                    .buttonStyle(.bordered)
                    .disabled(viewModel.connectionState == .disconnected)

                    Button("Refresh Status") {
                        Task {
                            await viewModel.refreshStatus()
                        }
                    }
                    .buttonStyle(.bordered)
                    .disabled(viewModel.connectionState != .connected)

                    statusBadge
                }

                if let bridgeState = viewModel.bridgeState {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Connected host: \(bridgeState.hostName)")
                        Text("LAN addresses: \(bridgeState.localAddresses.joined(separator: ", "))")
                            .foregroundStyle(.secondary)
                        Text("Shared folder: \(bridgeState.storageRoot)")
                            .foregroundStyle(.secondary)
                    }
                    .font(.system(size: 13, weight: .regular, design: .monospaced))
                }
            }
        }
    }

    private var statusSection: some View {
        SectionCard(title: "Windows Status") {
            if let status = viewModel.status {
                VStack(alignment: .leading, spacing: 14) {
                    Text("\(status.hostName) - \(status.operatingSystem)")
                        .foregroundStyle(.secondary)

                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 12)], spacing: 12) {
                        MetricTile(title: "CPU", value: "\(FormatterBridge.oneDecimal(status.cpuLoadPercent))%", detail: "Processor load")
                        MetricTile(title: "RAM", value: "\(FormatterBridge.oneDecimal(status.memoryUsedGb)) / \(FormatterBridge.oneDecimal(status.memoryTotalGb)) GB", detail: "\(FormatterBridge.zeroDecimal(status.memoryUsagePercent))% in use")
                        MetricTile(title: "Disk", value: "\(FormatterBridge.oneDecimal(status.diskUsedGb)) / \(FormatterBridge.oneDecimal(status.diskTotalGb)) GB", detail: "\(FormatterBridge.oneDecimal(status.diskFreeGb)) GB free")
                        MetricTile(
                            title: "GPU",
                            value: status.gpu?.name ?? "Not available",
                            detail: status.gpu?.memoryGb.map { "\(FormatterBridge.oneDecimal($0)) GB VRAM" } ?? "No GPU details reported"
                        )
                    }

                    Text("Last sample: \(status.sampledAt.formatted(date: .abbreviated, time: .standard))")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            } else {
                EmptyStateText("Connect to the Windows host to start the live dashboard.")
            }
        }
    }

    private var clipboardSection: some View {
        SectionCard(title: "Clipboard") {
            HStack(alignment: .top, spacing: 14) {
                VStack(alignment: .leading, spacing: 10) {
                    Text("Mac Clipboard")
                        .font(.headline)
                    TextBlock(text: viewModel.localClipboardText)
                    Button("Send Mac Clipboard to Windows") {
                        Task {
                            await viewModel.pushClipboardToWindows()
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(viewModel.connectionState != .connected)
                }

                VStack(alignment: .leading, spacing: 10) {
                    Text("Windows Clipboard")
                        .font(.headline)
                    TextBlock(text: viewModel.remoteClipboardText)
                    Button("Copy Windows Clipboard to Mac") {
                        Task {
                            await viewModel.pullClipboardFromWindows()
                        }
                    }
                    .buttonStyle(.bordered)
                    .disabled(viewModel.connectionState != .connected)
                }
            }
        }
    }

    private var controlMacFromWindowsSection: some View {
        SectionCard(title: "Control Mac from Windows") {
            VStack(alignment: .leading, spacing: 12) {
                Toggle("Control Mac from Windows", isOn: Binding(
                    get: { viewModel.isControlMacFromWindowsEnabled },
                    set: { newValue in
                        if newValue {
                            showingControlMacConsent = true
                        } else {
                            Task {
                                await viewModel.disableControlMacFromWindows()
                            }
                        }
                    }
                ))
                .disabled(viewModel.connectionState != .connected && !viewModel.isControlMacFromWindowsEnabled)

                HStack(spacing: 12) {
                    controlMacBadge
                    Text("Primary/default direction")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.secondary)
                }

                Text(viewModel.controlMacFromWindowsSummary)
                    .foregroundStyle(.secondary)

                Text("Activation edge on Windows: \(viewModel.controlMacActivationEdge)")
                    .foregroundStyle(.secondary)

                Text("Escape hotkey on Windows: \(viewModel.controlMacEscapeHotkey)")
                    .font(.system(.body, design: .monospaced))

                Text("Safety notes: this mode stays local-only, rides the existing main bridge connection, and keeps the older Mac-to-Windows path as an optional reverse mode.")
                    .foregroundStyle(.secondary)

                Text("Accessibility: macOS needs Accessibility permission so the Mac can inject mouse and keyboard input received from Windows.")
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var inputBridgeSection: some View {
        SectionCard(title: "Control Windows from Mac") {
            VStack(alignment: .leading, spacing: 12) {
                Toggle("Input Bridge Mode", isOn: Binding(
                    get: { viewModel.isInputBridgeModeEnabled },
                    set: { newValue in
                        if newValue {
                            showingInputBridgeConsent = true
                        } else {
                            viewModel.disableInputBridge()
                        }
                    }
                ))
                .disabled(viewModel.connectionState != .connected && !viewModel.isInputBridgeModeEnabled)

                HStack(spacing: 12) {
                    inputBridgeBadge

                    if viewModel.inputBridgePhase == .active {
                        Button("Return Control to Mac") {
                            viewModel.returnInputBridgeControlToMac()
                        }
                        .buttonStyle(.bordered)
                    }
                }

                Text(viewModel.inputBridgeStatusSummary)
                    .foregroundStyle(.secondary)

                Text("Reverse direction: when armed, move the Mac cursor to the right edge of the current screen to start controlling Windows.")
                    .foregroundStyle(.secondary)

                Text("Escape hotkey: Ctrl + Option + Command + Esc")
                    .font(.system(.body, design: .monospaced))

                Text("Safety notes: this first version stays local-only, injects events with native Windows APIs, and focuses on mouse plus common keyboard forwarding. Some less common keys may not map perfectly yet.")
                    .foregroundStyle(.secondary)

                Text("Accessibility: macOS will ask for Accessibility permission before this can capture global input events.")
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var filesSection: some View {
        SectionCard(title: "File Transfer") {
            VStack(alignment: .leading, spacing: 12) {
                HStack(spacing: 12) {
                    Button("Upload from Mac") {
                        showingFileImporter = true
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(viewModel.connectionState != .connected)

                    Button("Refresh Remote Files") {
                        Task {
                            await viewModel.refreshFiles()
                        }
                    }
                    .buttonStyle(.bordered)
                    .disabled(viewModel.connectionState != .connected || viewModel.isRefreshingFiles)
                }

                if viewModel.remoteFiles.isEmpty {
                    EmptyStateText("Remote files appear here after the first upload or refresh.")
                } else {
                    VStack(spacing: 0) {
                        ForEach(viewModel.remoteFiles) { entry in
                            HStack(spacing: 12) {
                                Image(systemName: entry.isDirectory ? "folder.fill" : "doc.fill")
                                    .foregroundStyle(entry.isDirectory ? .yellow : .accentColor)

                                VStack(alignment: .leading, spacing: 2) {
                                    Text(entry.relativePath)
                                        .font(.system(.body, design: .monospaced))
                                    Text("\(entry.lastModifiedAt.formatted(date: .abbreviated, time: .shortened))")
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                }

                                Spacer()

                                Text(entry.sizeBytes.map(FormatterBridge.byteCount) ?? "Folder")
                                    .foregroundStyle(.secondary)

                                if !entry.isDirectory {
                                    Button("Download") {
                                        Task {
                                            await viewModel.downloadFile(entry)
                                        }
                                    }
                                    .buttonStyle(.bordered)
                                }
                            }
                            .padding(.vertical, 8)

                            Divider()
                        }
                    }
                }
            }
        }
    }

    private var commandSection: some View {
        SectionCard(title: "Remote Command Runner") {
            VStack(alignment: .leading, spacing: 12) {
                Text("Commands are previewed first, and the Windows host blocks a few destructive tokens by default.")
                    .foregroundStyle(.secondary)

                Picker("Shell", selection: $viewModel.commandShell) {
                    ForEach(CommandShell.allCases) { shell in
                        Text(shell.rawValue).tag(shell)
                    }
                }
                .pickerStyle(.segmented)
                .frame(maxWidth: 320)

                TextEditor(text: $viewModel.commandText)
                    .font(.system(.body, design: .monospaced))
                    .frame(minHeight: 120)
                    .padding(8)
                    .background(Color(nsColor: .textBackgroundColor))
                    .clipShape(RoundedRectangle(cornerRadius: 12))

                if let preview = viewModel.commandPreview {
                    VStack(alignment: .leading, spacing: 6) {
                        Text(preview.blocked ? "Blocked" : "Preview")
                            .font(.headline)
                            .foregroundStyle(preview.blocked ? .red : .primary)

                        Text(viewModel.commandPreviewSummary)
                            .font(.system(.body, design: .monospaced))
                            .foregroundStyle(.secondary)
                    }
                    .padding(12)
                    .background(Color(nsColor: .controlBackgroundColor))
                    .clipShape(RoundedRectangle(cornerRadius: 12))
                }

                HStack(spacing: 12) {
                    Button("Preview Command") {
                        Task {
                            await viewModel.previewCurrentCommand()
                        }
                    }
                    .buttonStyle(.bordered)
                    .disabled(viewModel.connectionState != .connected)

                    Button("Run on Windows") {
                        Task {
                            await viewModel.previewCurrentCommand()
                            if viewModel.commandPreview?.blocked == false {
                                showingCommandConfirmation = true
                            }
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(viewModel.connectionState != .connected || viewModel.isRunningCommand)
                }

                if let result = viewModel.commandResult {
                    VStack(alignment: .leading, spacing: 10) {
                        Text("Last Result - Exit \(result.exitCode)")
                            .font(.headline)

                        if !result.warnings.isEmpty {
                            Text(result.warnings.joined(separator: "\n"))
                                .foregroundStyle(.orange)
                        }

                        if !result.standardOutput.isEmpty {
                            LabeledField(title: "Standard Output") {
                                TextBlock(text: result.standardOutput)
                            }
                        }

                        if !result.standardError.isEmpty {
                            LabeledField(title: "Standard Error") {
                                TextBlock(text: result.standardError)
                            }
                        }
                    }
                }
            }
        }
    }

    private var activitySection: some View {
        SectionCard(title: "Activity") {
            if viewModel.activityLog.isEmpty {
                EmptyStateText("Connection and sync events will show up here.")
            } else {
                VStack(alignment: .leading, spacing: 8) {
                    ForEach(viewModel.activityLog, id: \.self) { entry in
                        Text(entry)
                            .font(.system(.caption, design: .monospaced))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
            }
        }
    }

    private var statusBadge: some View {
        Text(viewModel.connectionState.label)
            .font(.caption.weight(.semibold))
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .background(viewModel.connectionState == .connected ? Color.green.opacity(0.2) : Color.orange.opacity(0.18))
            .clipShape(Capsule())
    }

    private var controlMacBadge: some View {
        Text(viewModel.controlMacFromWindowsPhase.label)
            .font(.caption.weight(.semibold))
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .background(controlMacBadgeColor.opacity(0.2))
            .clipShape(Capsule())
    }

    private var controlMacBadgeColor: Color {
        switch viewModel.controlMacFromWindowsPhase {
        case .off:
            return .orange
        case .armed:
            return .blue
        case .active:
            return .green
        }
    }

    private var inputBridgeBadge: some View {
        Text(viewModel.inputBridgePhase.label)
            .font(.caption.weight(.semibold))
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
            .background(inputBridgeBadgeColor.opacity(0.2))
            .clipShape(Capsule())
    }

    private var inputBridgeBadgeColor: Color {
        switch viewModel.inputBridgePhase {
        case .off:
            return .orange
        case .armed:
            return .blue
        case .active:
            return .green
        }
    }
}

private struct SectionCard<Content: View>: View {
    let title: String
    let content: Content

    init(title: String, @ViewBuilder content: () -> Content) {
        self.title = title
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(title)
                .font(.title3.weight(.semibold))

            content
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color(nsColor: .controlBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 18))
    }
}

private struct MetricTile: View {
    let title: String
    let value: String
    let detail: String

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title)
                .font(.headline)
                .foregroundStyle(.secondary)

            Text(value)
                .font(.system(size: 18, weight: .semibold, design: .rounded))

            Text(detail)
                .font(.footnote)
                .foregroundStyle(.secondary)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color(nsColor: .windowBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 14))
    }
}

private struct LabeledField<Content: View>: View {
    let title: String
    let content: Content

    init(title: String, @ViewBuilder content: () -> Content) {
        self.title = title
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
                .font(.caption)
                .foregroundStyle(.secondary)

            content
        }
    }
}

private struct TextBlock: View {
    let text: String

    var body: some View {
        ScrollView {
            Text(text.isEmpty ? "No text available" : text)
                .font(.system(.body, design: .monospaced))
                .frame(maxWidth: .infinity, alignment: .leading)
                .textSelection(.enabled)
        }
        .frame(minHeight: 110)
        .padding(10)
        .background(Color(nsColor: .textBackgroundColor))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }
}

private struct EmptyStateText: View {
    let message: String

    init(_ message: String) {
        self.message = message
    }

    var body: some View {
        Text(message)
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.vertical, 8)
    }
}

private enum FormatterBridge {
    static func byteCount(_ bytes: Int64) -> String {
        let formatter = ByteCountFormatter()
        formatter.countStyle = .file
        return formatter.string(fromByteCount: bytes)
    }

    static func oneDecimal(_ value: Double) -> String {
        String(format: "%.1f", value)
    }

    static func zeroDecimal(_ value: Double) -> String {
        String(format: "%.0f", value)
    }
}
