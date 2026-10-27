import SwiftUI

struct WelcomeView: View {
    let preferences: Preferences
    let state: AppState
    let actions: any SettingsActions
    let done: () -> Void

    var body: some View {
        VStack(spacing: 22) {
            Image(nsImage: NSApp.applicationIconImage)
                .resizable()
                .frame(width: 80, height: 80)
                .accessibilityHidden(true)

            VStack(spacing: 6) {
                Text("Welcome to Code Caller")
                    .font(.title2.bold())
                Text("Copy your newest 2FA code from Apple Mail with one shortcut, in any app.")
                    .multilineTextAlignment(.center)
                    .foregroundStyle(.secondary)
            }

            VStack(alignment: .leading, spacing: 16) {
                Step(number: 1, title: "Allow Mail access",
                     detail: "macOS will ask if Code Caller can control Mail. It only reads recent inbox messages, and only when you press the shortcut.") {
                    MailAccessStatus(access: state.mailAccess, allow: actions.allowMailAccess)
                }
                Step(number: 2, title: "Press \(preferences.shortcut.displayString)",
                     detail: "Your newest code is copied. Paste it with ⌘V.") {
                    EmptyView()
                }
            }

            Text("Code Caller has no internet access and never saves what it reads.")
                .font(.footnote)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)

            Button("Done", action: done)
                .keyboardShortcut(.defaultAction)
                .controlSize(.large)
        }
        .padding(28)
        .frame(width: 420)
    }
}

private struct Step<Accessory: View>: View {
    let number: Int
    let title: String
    let detail: String
    @ViewBuilder let accessory: () -> Accessory

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(number)")
                .font(.callout.bold())
                .frame(width: 24, height: 24)
                .background(.tint.opacity(0.15), in: Circle())
                .foregroundStyle(.tint)
            VStack(alignment: .leading, spacing: 6) {
                Text(title).font(.headline)
                Text(detail)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                accessory()
            }
        }
    }
}
