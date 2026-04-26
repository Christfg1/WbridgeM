import Foundation

final class SettingsStore {
    private let key = "BridgeMac.ConnectionSettings"

    func load() -> BridgeConnectionSettings {
        guard
            let data = UserDefaults.standard.data(forKey: key),
            let settings = try? JSONDecoder().decode(BridgeConnectionSettings.self, from: data)
        else {
            return BridgeConnectionSettings()
        }

        return settings
    }

    func save(_ settings: BridgeConnectionSettings) {
        guard let data = try? JSONEncoder().encode(settings) else { return }
        UserDefaults.standard.set(data, forKey: key)
    }
}
