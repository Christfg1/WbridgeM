import AppKit
import Foundation

final class ClipboardMonitor {
    private let pasteboard = NSPasteboard.general
    private var timer: Timer?
    private var lastChangeCount: Int

    init() {
        lastChangeCount = pasteboard.changeCount
    }

    func currentText() -> String {
        pasteboard.string(forType: .string) ?? ""
    }

    func setClipboardText(_ text: String) {
        pasteboard.clearContents()
        pasteboard.setString(text, forType: .string)
        lastChangeCount = pasteboard.changeCount
    }

    func start(onChange: @escaping (String) -> Void) {
        stop()

        timer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            guard let self else { return }
            let changeCount = self.pasteboard.changeCount
            guard changeCount != self.lastChangeCount else { return }

            self.lastChangeCount = changeCount
            onChange(self.currentText())
        }

        RunLoop.main.add(timer!, forMode: .common)
    }

    func stop() {
        timer?.invalidate()
        timer = nil
    }
}
