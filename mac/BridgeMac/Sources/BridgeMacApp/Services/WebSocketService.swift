import Foundation

final class WebSocketService {
    private let session = URLSession(configuration: .default)
    private var task: URLSessionWebSocketTask?
    private var receiveLoop: Task<Void, Never>?
    private var onEvent: ((Data) -> Void)?
    private var onDisconnect: ((String?) -> Void)?

    func connect(
        settings: BridgeConnectionSettings,
        onEvent: @escaping (Data) -> Void,
        onDisconnect: @escaping (String?) -> Void
    ) throws {
        disconnect()

        var components = URLComponents()
        components.scheme = "ws"
        components.host = settings.host.trimmingCharacters(in: .whitespacesAndNewlines)
        components.port = settings.port
        components.path = "/ws"

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

    func disconnect() {
        receiveLoop?.cancel()
        receiveLoop = nil

        task?.cancel(with: .normalClosure, reason: nil)
        task = nil
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
