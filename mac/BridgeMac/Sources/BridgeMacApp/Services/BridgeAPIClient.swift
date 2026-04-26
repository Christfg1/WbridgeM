import Foundation

final class BridgeAPIClient {
    private let session: URLSession

    init(session: URLSession = .shared) {
        self.session = session
    }

    func fetchBridgeState(settings: BridgeConnectionSettings) async throws -> BridgeState {
        let request = try authorizedRequest(path: "/api/bridge", settings: settings)
        return try await perform(request, decodeAs: BridgeState.self)
    }

    func fetchStatus(settings: BridgeConnectionSettings) async throws -> StatusSnapshot {
        let request = try authorizedRequest(path: "/api/status", settings: settings)
        return try await perform(request, decodeAs: StatusSnapshot.self)
    }

    func fetchClipboard(settings: BridgeConnectionSettings) async throws -> ClipboardContent {
        let request = try authorizedRequest(path: "/api/clipboard", settings: settings)
        return try await perform(request, decodeAs: ClipboardContent.self)
    }

    func setClipboard(text: String, sourceDevice: String?, settings: BridgeConnectionSettings) async throws -> ClipboardContent {
        var request = try authorizedRequest(path: "/api/clipboard", method: "POST", settings: settings)
        request.httpBody = try BridgeJSONCoding.encoder.encode(SetClipboardRequest(text: text, sourceDevice: sourceDevice))
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        return try await perform(request, decodeAs: ClipboardContent.self)
    }

    func listFiles(settings: BridgeConnectionSettings) async throws -> FileListResponse {
        let request = try authorizedRequest(path: "/api/files", settings: settings)
        return try await perform(request, decodeAs: FileListResponse.self)
    }

    func uploadFile(fileURL: URL, subdirectory: String?, settings: BridgeConnectionSettings) async throws -> UploadFileResponse {
        var request = try authorizedRequest(path: "/api/files/upload", method: "POST", settings: settings)
        let multipart = try makeMultipartBody(fileURL: fileURL, subdirectory: subdirectory)
        request.httpBody = multipart.body
        request.setValue("multipart/form-data; boundary=\(multipart.boundary)", forHTTPHeaderField: "Content-Type")
        return try await perform(request, decodeAs: UploadFileResponse.self)
    }

    func downloadFile(relativePath: String, settings: BridgeConnectionSettings) async throws -> Data {
        let request = try authorizedRequest(path: "/api/files/download", queryItems: [URLQueryItem(name: "relativePath", value: relativePath)], settings: settings)
        let (data, response) = try await session.data(for: request)
        try validate(response: response, data: data)
        return data
    }

    func previewCommand(command: String, shell: CommandShell, settings: BridgeConnectionSettings) async throws -> CommandPreviewResponse {
        var request = try authorizedRequest(path: "/api/commands/preview", method: "POST", settings: settings)
        request.httpBody = try BridgeJSONCoding.encoder.encode(CommandPreviewRequest(command: command, shell: shell))
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        return try await perform(request, decodeAs: CommandPreviewResponse.self)
    }

    func runCommand(command: String, shell: CommandShell, settings: BridgeConnectionSettings) async throws -> RunCommandResponse {
        var request = try authorizedRequest(path: "/api/commands/run", method: "POST", settings: settings)
        request.httpBody = try BridgeJSONCoding.encoder.encode(RunCommandRequest(command: command, shell: shell, confirmed: true))
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        return try await perform(request, decodeAs: RunCommandResponse.self)
    }

    func fetchControlMacFromWindowsState(settings: BridgeConnectionSettings) async throws -> ControlMacFromWindowsState {
        let request = try authorizedRequest(path: "/api/input/control-mac", settings: settings)
        return try await perform(request, decodeAs: ControlMacFromWindowsState.self)
    }

    func setControlMacFromWindows(enabled: Bool, settings: BridgeConnectionSettings) async throws -> ControlMacFromWindowsState {
        var request = try authorizedRequest(path: "/api/input/control-mac", method: "POST", settings: settings)
        request.httpBody = try BridgeJSONCoding.encoder.encode(ControlMacFromWindowsRequest(enabled: enabled))
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        return try await perform(request, decodeAs: ControlMacFromWindowsState.self)
    }

    private func perform<Response: Decodable>(_ request: URLRequest, decodeAs type: Response.Type) async throws -> Response {
        let (data, response) = try await session.data(for: request)
        try validate(response: response, data: data)
        return try BridgeJSONCoding.decoder.decode(Response.self, from: data)
    }

    private func validate(response: URLResponse, data: Data) throws {
        guard let httpResponse = response as? HTTPURLResponse else {
            throw BridgeClientError.invalidResponse
        }

        guard (200..<300).contains(httpResponse.statusCode) else {
            if let serverError = try? BridgeJSONCoding.decoder.decode(RemoteErrorResponse.self, from: data) {
                throw BridgeClientError.server(message: serverError.error)
            }

            throw BridgeClientError.server(message: "Unexpected HTTP status \(httpResponse.statusCode).")
        }
    }

    private func authorizedRequest(
        path: String,
        method: String = "GET",
        queryItems: [URLQueryItem] = [],
        settings: BridgeConnectionSettings
    ) throws -> URLRequest {
        guard !settings.host.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw BridgeClientError.invalidConfiguration("Enter the Windows host or IP address first.")
        }

        guard !settings.sharedSecret.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw BridgeClientError.invalidConfiguration("Enter the shared secret before connecting.")
        }

        var components = URLComponents()
        components.scheme = "http"
        components.host = settings.host.trimmingCharacters(in: .whitespacesAndNewlines)
        components.port = settings.port
        components.path = path
        if !queryItems.isEmpty {
            components.queryItems = queryItems
        }

        guard let url = components.url else {
            throw BridgeClientError.invalidConfiguration("The bridge URL is not valid.")
        }

        var request = URLRequest(url: url)
        request.httpMethod = method
        request.timeoutInterval = 30
        request.setValue(settings.sharedSecret, forHTTPHeaderField: "X-Bridge-Secret")
        return request
    }

    private func makeMultipartBody(fileURL: URL, subdirectory: String?) throws -> (boundary: String, body: Data) {
        let boundary = "BridgeBoundary-\(UUID().uuidString)"
        let fileName = fileURL.lastPathComponent
        let fileData = try Data(contentsOf: fileURL)

        // A tiny hand-rolled multipart builder keeps the Mac client dependency-free for v1.
        var body = Data()

        if let subdirectory, !subdirectory.isEmpty {
            body.appendString("--\(boundary)\r\n")
            body.appendString("Content-Disposition: form-data; name=\"subdirectory\"\r\n\r\n")
            body.appendString("\(subdirectory)\r\n")
        }

        body.appendString("--\(boundary)\r\n")
        body.appendString("Content-Disposition: form-data; name=\"file\"; filename=\"\(fileName)\"\r\n")
        body.appendString("Content-Type: application/octet-stream\r\n\r\n")
        body.append(fileData)
        body.appendString("\r\n--\(boundary)--\r\n")
        return (boundary, body)
    }
}

enum BridgeClientError: LocalizedError {
    case invalidConfiguration(String)
    case invalidResponse
    case notConnected(String)
    case permissionRequired(String)
    case server(message: String)

    var errorDescription: String? {
        switch self {
        case let .invalidConfiguration(message):
            return message
        case .invalidResponse:
            return "The Windows bridge returned an invalid response."
        case let .notConnected(message):
            return message
        case let .permissionRequired(message):
            return message
        case let .server(message):
            return message
        }
    }
}

private extension Data {
    mutating func appendString(_ value: String) {
        append(Data(value.utf8))
    }
}
