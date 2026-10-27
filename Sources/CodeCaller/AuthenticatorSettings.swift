import CodeCallerCore
import SwiftUI

struct AuthenticatorSettings: View {
    @Bindable var preferences: Preferences
    let state: AppState
    let vault: Vault
    let actions: any SettingsActions

    @State private var isAddingAccount = false
    @State private var pendingDeletion: OTPAccount?

    var body: some View {
        Form {
            Section {
                LabeledContent("Shortcut") {
                    ShortcutButton(kind: .authenticator, shortcut: preferences.authenticatorShortcut, state: state, actions: actions)
                }
                Picker("Ask for Touch ID", selection: $preferences.vaultUnlockSeconds) {
                    Text("Every time").tag(0)
                    Text("After 5 minutes").tag(300)
                    Text("After 15 minutes").tag(900)
                }
            } footer: {
                Text("Press the shortcut, type part of an account's name and press Return to copy its code. Secrets stay in your keychain on this Mac.")
                    .foregroundStyle(.secondary)
            }

            Section {
                if vault.isUnlocked {
                    if vault.accounts.isEmpty {
                        Text("No accounts yet.")
                            .foregroundStyle(.secondary)
                    }
                    ForEach(vault.accounts) { account in
                        HStack {
                            Text(account.displayName)
                                .lineLimit(1)
                            if account.kind == .hotp {
                                Text("Counter-based")
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer()
                            Button {
                                pendingDeletion = account
                            } label: {
                                Image(systemName: "trash")
                            }
                            .buttonStyle(.borderless)
                            .help("Delete \(account.displayName)")
                            .accessibilityLabel("Delete \(account.displayName)")
                        }
                    }
                    HStack {
                        Menu("Add Accounts") {
                            Button("Import File…", action: actions.importAccounts)
                            Button("Import from 2fa…", action: actions.importFromTwoFA)
                            Button("Paste Link or QR Code", action: actions.pasteAccounts)
                            Divider()
                            Button("Enter Setup Key…") { isAddingAccount = true }
                        }
                        .fixedSize()
                        Spacer()
                        Button("Lock", action: actions.lockVault)
                    }
                } else {
                    HStack {
                        Label("Locked", systemImage: "lock.fill")
                            .foregroundStyle(.secondary)
                        Spacer()
                        Button("Unlock…", action: actions.unlockVault)
                    }
                }
            } header: {
                Text("Accounts")
            } footer: {
                Text("Import QR code screenshots, Google Authenticator transfer codes, otpauth links, the 2fa command-line tool's keys, and exports from Apple Passwords, Bitwarden, 1Password, Aegis, 2FAS and andOTP. Authy and Duo can't export accounts, so add those again by scanning a new QR code from each website.")
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .sheet(isPresented: $isAddingAccount) {
            AddAccountSheet { actions.addAccount($0) }
        }
        .confirmationDialog(
            "Delete \(pendingDeletion?.displayName ?? "this account")?",
            isPresented: Binding(get: { pendingDeletion != nil }, set: { if !$0 { pendingDeletion = nil } })
        ) {
            Button("Delete", role: .destructive) {
                if let pendingDeletion { actions.deleteAccount(pendingDeletion) }
            }
        } message: {
            Text("You'll need to set it up again to get its codes from Code Caller.")
        }
    }
}

private struct AddAccountSheet: View {
    let add: (OTPAccount) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var issuer = ""
    @State private var name = ""
    @State private var setupKey = ""
    @State private var kind = OTPKind.totp

    /// The decoded key, if it's long enough to be a real one.
    private var secret: Data? {
        Base32.decode(setupKey).flatMap { $0.count >= OTPAccount.minimumSecretLength ? $0 : nil }
    }

    var body: some View {
        VStack(alignment: .trailing, spacing: 0) {
            Form {
                Section {
                    TextField("Website or app", text: $issuer)
                    TextField("Account", text: $name, prompt: Text("Optional"))
                    SecureField("Setup key", text: $setupKey)
                    Picker("Code type", selection: $kind) {
                        Text("Time-based (most websites)").tag(OTPKind.totp)
                        Text("Counter-based").tag(OTPKind.hotp)
                    }
                } header: {
                    Text("Add an account")
                } footer: {
                    if !setupKey.isEmpty && secret == nil {
                        Text("That doesn't look like a setup key. They're usually 16 or 32 characters long, using only the letters A to Z and the numbers 2 to 7.")
                            .foregroundStyle(.red)
                    } else {
                        Text("Websites show a setup key next to the QR code, often behind \"Can't scan it?\".")
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .formStyle(.grouped)
            HStack {
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Add") {
                    if let secret { add(OTPAccount(issuer: issuer, name: name, secret: secret, kind: kind)) }
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
                .disabled(secret == nil || issuer.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            .padding([.horizontal, .bottom], 20)
        }
        .frame(width: 400)
    }
}
