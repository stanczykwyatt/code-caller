import Foundation
import Observation

/// Settings, kept in the app's own sandboxed preferences. Nothing read from Mail is ever stored.
@MainActor
@Observable
final class Preferences {
    @ObservationIgnored private let defaults: UserDefaults

    var shortcut: Shortcut {
        didSet { defaults.set(try? JSONEncoder().encode(shortcut), forKey: Key.shortcut) }
    }
    var lookbackMinutes: Int {
        didSet { defaults.set(lookbackMinutes, forKey: Key.lookbackMinutes) }
    }
    /// Zero means the clipboard is never cleared.
    var clearClipboardSeconds: Int {
        didSet { defaults.set(clearClipboardSeconds, forKey: Key.clearClipboardSeconds) }
    }
    var showsCodeInNotice: Bool {
        didSet { defaults.set(showsCodeInNotice, forKey: Key.showsCodeInNotice) }
    }
    var checksForNewMail: Bool {
        didSet { defaults.set(checksForNewMail, forKey: Key.checksForNewMail) }
    }
    var hasSeenWelcome: Bool {
        didSet { defaults.set(hasSeenWelcome, forKey: Key.hasSeenWelcome) }
    }

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        defaults.register(defaults: [
            Key.lookbackMinutes: 10,
            Key.clearClipboardSeconds: 60,
            Key.showsCodeInNotice: true,
            Key.checksForNewMail: true,
        ])
        shortcut = defaults.data(forKey: Key.shortcut).flatMap { try? JSONDecoder().decode(Shortcut.self, from: $0) } ?? .standard
        lookbackMinutes = defaults.integer(forKey: Key.lookbackMinutes)
        clearClipboardSeconds = defaults.integer(forKey: Key.clearClipboardSeconds)
        showsCodeInNotice = defaults.bool(forKey: Key.showsCodeInNotice)
        checksForNewMail = defaults.bool(forKey: Key.checksForNewMail)
        hasSeenWelcome = defaults.bool(forKey: Key.hasSeenWelcome)
    }

    private enum Key {
        static let shortcut = "shortcut"
        static let lookbackMinutes = "lookbackMinutes"
        static let clearClipboardSeconds = "clearClipboardSeconds"
        static let showsCodeInNotice = "showsCodeInNotice"
        static let checksForNewMail = "checksForNewMail"
        static let hasSeenWelcome = "hasSeenWelcome"
    }
}
