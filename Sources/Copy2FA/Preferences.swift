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
    var authenticatorShortcut: Shortcut {
        didSet { defaults.set(try? JSONEncoder().encode(authenticatorShortcut), forKey: Key.authenticatorShortcut) }
    }
    /// Off until you record one, so Copy 2FA doesn't take over another key combination by itself.
    var recentCodesShortcut: Shortcut? {
        didSet {
            if let recentCodesShortcut {
                defaults.set(try? JSONEncoder().encode(recentCodesShortcut), forKey: Key.recentCodesShortcut)
            } else {
                defaults.removeObject(forKey: Key.recentCodesShortcut)
            }
        }
    }
    /// How long the authenticator stays unlocked after Touch ID. Zero asks every time.
    var vaultUnlockSeconds: Int {
        didSet { defaults.set(vaultUnlockSeconds, forKey: Key.vaultUnlockSeconds) }
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
    var readsMail: Bool {
        didSet { defaults.set(readsMail, forKey: Key.readsMail) }
    }
    /// Off until turned on, since it needs Full Disk Access.
    var readsMessages: Bool {
        didSet { defaults.set(readsMessages, forKey: Key.readsMessages) }
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
            Key.readsMail: true,
            Key.readsMessages: false,
        ])
        shortcut = defaults.data(forKey: Key.shortcut).flatMap { try? JSONDecoder().decode(Shortcut.self, from: $0) } ?? .standard
        authenticatorShortcut = defaults.data(forKey: Key.authenticatorShortcut)
            .flatMap { try? JSONDecoder().decode(Shortcut.self, from: $0) } ?? .authenticator
        recentCodesShortcut = defaults.data(forKey: Key.recentCodesShortcut).flatMap { try? JSONDecoder().decode(Shortcut.self, from: $0) }
        vaultUnlockSeconds = defaults.integer(forKey: Key.vaultUnlockSeconds)
        lookbackMinutes = defaults.integer(forKey: Key.lookbackMinutes)
        clearClipboardSeconds = defaults.integer(forKey: Key.clearClipboardSeconds)
        showsCodeInNotice = defaults.bool(forKey: Key.showsCodeInNotice)
        checksForNewMail = defaults.bool(forKey: Key.checksForNewMail)
        readsMail = defaults.bool(forKey: Key.readsMail)
        readsMessages = defaults.bool(forKey: Key.readsMessages)
        hasSeenWelcome = defaults.bool(forKey: Key.hasSeenWelcome)
    }

    var lookupOptions: LookupOptions {
        LookupOptions(
            lookbackMinutes: lookbackMinutes,
            readsMail: readsMail,
            readsMessages: readsMessages,
            checksForNewMail: checksForNewMail
        )
    }

    private enum Key {
        static let shortcut = "shortcut"
        static let authenticatorShortcut = "authenticatorShortcut"
        static let recentCodesShortcut = "recentCodesShortcut"
        static let vaultUnlockSeconds = "vaultUnlockSeconds"
        static let lookbackMinutes = "lookbackMinutes"
        static let clearClipboardSeconds = "clearClipboardSeconds"
        static let showsCodeInNotice = "showsCodeInNotice"
        static let checksForNewMail = "checksForNewMail"
        static let readsMail = "readsMail"
        static let readsMessages = "readsMessages"
        static let hasSeenWelcome = "hasSeenWelcome"
    }
}
