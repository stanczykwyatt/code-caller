import Observation
import ServiceManagement

/// What the app knows right now, shown in its windows. Never saved.
@MainActor
@Observable
final class AppState {
    var mailAccess = MailAccess.unknown
    var canReadMessages = false
    var isRecordingShortcut = false
    var shortcutIsTaken = false
    var opensAtLogin = SMAppService.mainApp.status == .enabled
}

/// What the settings and welcome windows can ask the app to do.
@MainActor
protocol SettingsActions: AnyObject {
    func startRecordingShortcut()
    func stopRecordingShortcut()
    func allowMailAccess()
    func openFullDiskAccessSettings()
    func setOpensAtLogin(_ enabled: Bool)
}
