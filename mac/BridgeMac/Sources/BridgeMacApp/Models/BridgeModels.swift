import Foundation

struct BridgeConnectionSettings: Codable {
    var host: String = ""
    var port: Int = 5055
    var sharedSecret: String = ""
    var autoSyncClipboard: Bool = true
}

enum InputBridgePhase: String, Codable {
    case off = "Off"
    case armed = "Armed"
    case active = "Active"

    var label: String {
        switch self {
        case .off:
            return "Off"
        case .armed:
            return "Armed"
        case .active:
            return "Active"
        }
    }
}

struct BridgeState: Decodable {
    let hostName: String
    let appVersion: String
    let localAddresses: [String]
    let storageRoot: String
    let webSocketPath: String
}

struct StatusSnapshot: Decodable {
    let hostName: String
    let operatingSystem: String
    let sampledAt: Date
    let cpuLoadPercent: Double
    let memoryUsedGb: Double
    let memoryTotalGb: Double
    let diskUsedGb: Double
    let diskTotalGb: Double
    let diskFreeGb: Double
    let gpu: GpuSnapshot?

    var memoryUsagePercent: Double {
        guard memoryTotalGb > 0 else { return 0 }
        return (memoryUsedGb / memoryTotalGb) * 100
    }

    var diskUsagePercent: Double {
        guard diskTotalGb > 0 else { return 0 }
        return (diskUsedGb / diskTotalGb) * 100
    }
}

struct GpuSnapshot: Decodable {
    let name: String
    let memoryGb: Double?
    let loadPercent: Double?
}

struct ClipboardContent: Decodable {
    let text: String
    let updatedAt: Date
    let sourceDevice: String?
}

struct SetClipboardRequest: Encodable {
    let text: String
    let sourceDevice: String?
}

struct FileListResponse: Decodable {
    let rootDirectory: String
    let entries: [FileEntry]
}

struct FileEntry: Decodable, Identifiable {
    let relativePath: String
    let isDirectory: Bool
    let sizeBytes: Int64?
    let lastModifiedAt: Date

    var id: String { relativePath }

    var fileName: String {
        URL(fileURLWithPath: relativePath).lastPathComponent
    }
}

enum CommandShell: String, Codable, CaseIterable, Identifiable {
    case powerShell = "PowerShell"
    case cmd = "Cmd"

    var id: String { rawValue }
}

struct CommandPreviewRequest: Encodable {
    let command: String
    let shell: CommandShell
}

struct CommandPreviewResponse: Decodable {
    let normalizedCommand: String
    let shell: CommandShell
    let blocked: Bool
    let blockedReason: String?
    let warnings: [String]
}

struct RunCommandRequest: Encodable {
    let command: String
    let shell: CommandShell
    let confirmed: Bool
}

struct RunCommandResponse: Decodable {
    let command: String
    let shell: CommandShell
    let startedAt: Date
    let finishedAt: Date
    let exitCode: Int
    let standardOutput: String
    let standardError: String
    let warnings: [String]
}

struct UploadFileResponse: Decodable {
    let relativePath: String
    let sizeBytes: Int64
    let uploadedAt: Date
}

struct EventTypeProbe: Decodable {
    let type: String
}

struct BridgeEventEnvelope<Payload: Decodable>: Decodable {
    let type: String
    let occurredAt: Date
    let payload: Payload
}

struct RemoteErrorResponse: Decodable {
    let error: String
}

struct ControlMacFromWindowsRequest: Encodable {
    let enabled: Bool
}

struct ControlMacFromWindowsState: Decodable {
    let enabled: Bool
    let phase: InputBridgePhase
    let activationEdge: String
    let escapeHotkey: String
    let requiresMacAccessibilityPermission: Bool
}

enum InputBridgeEventKind: String, Codable {
    case mouseMove = "MouseMove"
    case mouseButton = "MouseButton"
    case scroll = "Scroll"
    case key = "Key"
}

enum InputBridgeMouseButton: String, Codable {
    case left = "Left"
    case right = "Right"
    case middle = "Middle"
}

struct InputBridgeEvent: Codable {
    let kind: InputBridgeEventKind
    let deltaX: Int
    let deltaY: Int
    let button: InputBridgeMouseButton?
    let isDown: Bool?
    let scrollX: Int
    let scrollY: Int
    let windowsVirtualKey: UInt16?

    init(
        kind: InputBridgeEventKind,
        deltaX: Int = 0,
        deltaY: Int = 0,
        button: InputBridgeMouseButton? = nil,
        isDown: Bool? = nil,
        scrollX: Int = 0,
        scrollY: Int = 0,
        windowsVirtualKey: UInt16? = nil
    ) {
        self.kind = kind
        self.deltaX = deltaX
        self.deltaY = deltaY
        self.button = button
        self.isDown = isDown
        self.scrollX = scrollX
        self.scrollY = scrollY
        self.windowsVirtualKey = windowsVirtualKey
    }
}

struct InputBridgeSocketMessage: Encodable {
    let type: String
    let payload: InputBridgeEvent?
}
