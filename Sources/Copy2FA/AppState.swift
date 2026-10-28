import Copy2FACore
import Observation
import ServiceManagement

enum ShortcutKind: Sendable {
    case copyCode, authenticator
}

/// What the app knows right now, shown in its windows. Never saved.
@MainActor
@Observable
final class AppState {
    var mailAccess = MailAccess.unknown
    var canReadMessages = false
    var recordingShortcut: ShortcutKind?
    var takenShortcuts: Set<ShortcutKind> = []
    var opensAtLogin = SMAppService.mainApp.status == .enabled
}

/// What the settings and welcome windows can ask the app to do.
@MainActor
protocol SettingsActions: AnyObject {
    func startRecordingShortcut(_ kind: ShortcutKind)
    func stopRecordingShortcut()
    func allowMailAccess()
    func openFullDiskAccessSettings()
    func setOpensAtLogin(_ enabled: Bool)
    func unlockVault()
    func lockVault()
    func importAccounts()
    func importFromTwoFA()
    func pasteAccounts()
    func addAccount(_ account: OTPAccount)
    func deleteAccount(_ account: OTPAccount)
}
