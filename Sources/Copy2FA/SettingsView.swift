import SwiftUI

struct SettingsView: View {
    @Bindable var preferences: Preferences
    let state: AppState
    let vault: Vault
    let actions: any SettingsActions

    var body: some View {
        TabView {
            GeneralSettings(preferences: preferences, state: state, actions: actions)
                .tabItem { Label("General", systemImage: "gearshape") }
            AuthenticatorSettings(preferences: preferences, state: state, vault: vault, actions: actions)
                .tabItem { Label("Authenticator", systemImage: CodeSource.authenticator.symbol) }
        }
        .frame(width: 480)
        .fixedSize(horizontal: false, vertical: true)
    }
}

private struct GeneralSettings: View {
    @Bindable var preferences: Preferences
    let state: AppState
    let actions: any SettingsActions

    var body: some View {
        Form {
            Section {
                LabeledContent("Shortcut") {
                    ShortcutButton(kind: .copyCode, shortcut: preferences.shortcut, state: state, actions: actions)
                }
                Picker("Look back", selection: $preferences.lookbackMinutes) {
                    ForEach([5, 10, 15, 30], id: \.self) { Text("\($0) minutes").tag($0) }
                }
                Toggle("Check for new mail when no code is found", isOn: $preferences.checksForNewMail)
            } footer: {
                Text("Press the shortcut in any app to copy your newest code.")
                    .foregroundStyle(.secondary)
            }

            Section {
                Toggle("Mail", isOn: $preferences.readsMail)
                if preferences.readsMail {
                    LabeledContent("Mail access") {
                        MailAccessStatus(access: state.mailAccess, allow: actions.allowMailAccess)
                    }
                }
                Toggle("Messages", isOn: $preferences.readsMessages)
                if preferences.readsMessages {
                    LabeledContent("Full Disk Access") {
                        if state.canReadMessages {
                            AllowedLabel()
                        } else {
                            Button("Open System Settings…", action: actions.openFullDiskAccessSettings)
                        }
                    }
                }
            } header: {
                Text("Look for codes in")
            } footer: {
                Text(messagesFooter)
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
                Toggle("Open at login", isOn: Binding(get: { state.opensAtLogin }, set: { actions.setOpensAtLogin($0) }))
            }

            Section {
                Text("Copy 2FA has no internet access. It only reads Mail and Messages when you press the shortcut, and never saves anything it reads.")
                    .foregroundStyle(.secondary)
                LabeledContent("Version", value: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "Development")
                Link("Source code on GitHub", destination: URL(string: "https://github.com/stanczykwyatt/copy-2fa")!)
            }
        }
        .formStyle(.grouped)
    }

    private var messagesFooter: String {
        guard preferences.readsMessages, !state.canReadMessages else {
            return "Reading texts needs Full Disk Access. Copy 2FA's sandbox still keeps it out of everything except your Messages database."
        }
        return "In System Settings, click +, choose Copy 2FA and turn it on. Copy 2FA's sandbox still keeps it out of everything except your Messages database."
    }
}

struct ShortcutButton: View {
    let kind: ShortcutKind
    let shortcut: Shortcut
    let state: AppState
    let actions: any SettingsActions

    var body: some View {
        HStack(spacing: 8) {
            if state.takenShortcuts.contains(kind) {
                Image(systemName: "exclamationmark.triangle.fill")
                    .foregroundStyle(.orange)
                    .help("Another app is using this shortcut. Pick a different one.")
                    .accessibilityLabel("Another app is using this shortcut")
            }
            Button(state.recordingShortcut == kind ? "Type a shortcut…" : shortcut.displayString) {
                if state.recordingShortcut == kind {
                    actions.stopRecordingShortcut()
                } else {
                    actions.startRecordingShortcut(kind)
                }
            }
            .frame(minWidth: 120)
        }
    }
}

struct AllowedLabel: View {
    var body: some View {
        HStack(spacing: 4) {
            Image(systemName: "checkmark.circle.fill").foregroundStyle(.green)
            Text("Allowed")
        }
    }
}

struct MailAccessStatus: View {
    let access: MailAccess
    let allow: () -> Void

    var body: some View {
        switch access {
        case .allowed:
            AllowedLabel()
        case .denied:
            Button("Open System Settings…", action: allow)
        case .unknown:
            Button("Allow…", action: allow)
        }
    }
}
