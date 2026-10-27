import SwiftUI

struct SettingsView: View {
    @Bindable var preferences: Preferences
    let state: AppState
    let actions: any SettingsActions

    var body: some View {
        Form {
            Section {
                LabeledContent("Shortcut") {
                    HStack(spacing: 8) {
                        if state.shortcutIsTaken {
                            Image(systemName: "exclamationmark.triangle.fill")
                                .foregroundStyle(.orange)
                                .help("Another app is using this shortcut. Pick a different one.")
                        }
                        Button(state.isRecordingShortcut ? "Type a shortcut…" : preferences.shortcut.displayString) {
                            if state.isRecordingShortcut {
                                actions.stopRecordingShortcut()
                            } else {
                                actions.startRecordingShortcut()
                            }
                        }
                        .frame(minWidth: 120)
                    }
                }
                Picker("Look back", selection: $preferences.lookbackMinutes) {
                    ForEach([5, 10, 15, 30], id: \.self) { Text("\($0) minutes").tag($0) }
                }
                Toggle("Check for new mail when no code is found", isOn: $preferences.checksForNewMail)
            } footer: {
                Text("Press the shortcut in any app to copy your newest code.")
                    .foregroundStyle(.secondary)
            }

            Section("Privacy") {
                Picker("Clear clipboard after", selection: $preferences.clearClipboardSeconds) {
                    Text("30 seconds").tag(30)
                    Text("1 minute").tag(60)
                    Text("2 minutes").tag(120)
                    Text("Never").tag(0)
                }
                Toggle("Show the code in the notice", isOn: $preferences.showsCodeInNotice)
            }

            Section {
                LabeledContent("Mail access") {
                    MailAccessStatus(access: state.mailAccess, allow: actions.allowMailAccess)
                }
                Toggle("Open at login", isOn: Binding(get: { state.opensAtLogin }, set: { actions.setOpensAtLogin($0) }))
            }

            Section {
                Text("Code Caller has no internet access. It only reads Mail when you press the shortcut, and never saves anything it reads.")
                    .foregroundStyle(.secondary)
                LabeledContent("Version", value: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "Development")
                Link("Source code on GitHub", destination: URL(string: "https://github.com/stanczykwyatt/code-caller")!)
            }
        }
        .formStyle(.grouped)
        .frame(width: 460)
        .fixedSize(horizontal: false, vertical: true)
        .onDisappear { actions.stopRecordingShortcut() }
    }
}

struct MailAccessStatus: View {
    let access: MailAccess
    let allow: () -> Void

    var body: some View {
        switch access {
        case .allowed:
            HStack(spacing: 4) {
                Image(systemName: "checkmark.circle.fill").foregroundStyle(.green)
                Text("Allowed")
            }
        case .denied:
            Button("Open System Settings…", action: allow)
        case .notAsked, .unknown:
            Button("Allow…", action: allow)
        }
    }
}
