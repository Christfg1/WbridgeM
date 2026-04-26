import Foundation

final class WebSocketService {
    private let session = URLSession(configuration: .default)
    private let sendQueue = DispatchQueue(label: "BridgeMac.WebSocketService.SendQueue")
    private var task: URLSessionWebSocketTask?
    private var receiveLoop: Task<Void, Never>?
    private var onEvent: ((Data) -> Void)?
    private var onDisconnect: ((String?) -> Void)?

    func connect(
        settings: BridgeConnectionSettings,
        path: String = "/ws",
        onEvent: @escaping (Data) -> Void = { _ in },
        onDisconnect: @escaping (String?) -> Void = { _ in }
    ) throws {
        disconnect()

        var components = URLComponents()
        components.scheme = "ws"
        components.host = settings.host.trimmingCharacters(in: .whitespacesAndNewlines)
        components.port = settings.port
        components.path = path

        guard let url = components.url else {
            throw BridgeClientError.invalidConfiguration("The WebSocket URL is not valid.")
        }

        var request = URLRequest(url: url)
        request.setValue(settings.sharedSecret, forHTTPHeaderField: "X-Bridge-Secret")

        self.onEvent = onEvent
        self.onDisconnect = onDisconnect

        let task = session.webSocketTask(with: request)
        self.task = task
        task.resume()

        receiveLoop = Task { [weak self] in
            await self?.receiveMessages()
        }
    }

    func send(_ data: Data) async throws {
        guard let task else {
            throw BridgeClientError.notConnected("The WebSocket is not connected.")
        }

        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            sendQueue.async {
                task.send(.data(data)) { error in
                    if let error {
                        continuation.resume(throwing: error)
                    } else {
                        continuation.resume()
                    }
                }
            }
        }
    }

    func disconnect() {
        receiveLoop?.cancel()
        receiveLoop = nil

        task?.cancel(with: .normalClosure, reason: nil)
        task = nil
        onEvent = nil
        onDisconnect = nil
    }

    private func receiveMessages() async {
        while !Task.isCancelled, let task {
            do {
                let message = try await task.receive()
                switch message {
                case let .data(data):
                    onEvent?(data)
                case let .string(text):
                    onEvent?(Data(text.utf8))
                @unknown default:
                    break
                }
            } catch {
                if !Task.isCancelled {
                    onDisconnect?(error.localizedDescription)
                }
                break
            }
        }
    }
}
