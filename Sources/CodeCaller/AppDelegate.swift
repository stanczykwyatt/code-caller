import AppKit
import Carbon.HIToolbox
import CodeCallerCore
import ServiceManagement
import SwiftUI
import UniformTypeIdentifiers

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let preferences = Preferences()
    private let state = AppState()
    private let vault = Vault()
    private let hotKey = HotKey(id: 1)
    private let authenticatorHotKey = HotKey(id: 2)
    private let clipboard = Clipboard()
    private lazy var confirmation = ConfirmationPanel()
    private lazy var picker = AuthenticatorPicker()
    private var statusItem: NSStatusItem?
    private var copyItem: NSMenuItem?
    private var authenticatorItem: NSMenuItem?
    private var settingsWindow: NSWindow?
    private var welcomeWindow: NSWindow?
    private var lookup: Task<Void, Never>?
    private var shortcutRecorder: Any?
    private var isCopyingAuthenticatorCode = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = makeStatusItem()
        hotKey.onPress = { [weak self] in self?.copyLatestCode() }
        authenticatorHotKey.onPress = { [weak self] in self?.openAuthenticator() }
        picker.onCopy = { [weak self] account in self?.copyAuthenticatorCode(account) }
        picker.onClose = { [weak self] in
            guard let self else { return }
            vault.lock(after: unlockHoldSeconds)
        }
        // Nothing that shows codes may outlive the vault's lock.
        vault.onLock = { [weak self] in self?.picker.close() }
        registerShortcuts()
        if !preferences.hasSeenWelcome {
            showWelcome()
        }
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        // Picks up a change made in System Settings while Code Caller was in the background.
        refreshMailAccess()
        state.canReadMessages = MessagesStore.canRead()
    }

    func applicationWillTerminate(_ notification: Notification) {
        clipboard.clearIfUnchanged()
        vault.lock()
    }

    // MARK: - Copying codes

    @objc private func copyLatestCode() {
        // Pressing the shortcut again while a search is running does nothing.
        guard lookup == nil else { return }
        // Only show progress if the search takes long enough to notice.
        let progress = Task {
            try? await Task.sleep(for: .milliseconds(400))
            if !Task.isCancelled { confirmation.show(.looking) }
        }
        let options = preferences.lookupOptions
        lookup = Task {
            let result = await CodeFinder.findLatest(options)
            progress.cancel()
            show(result, options: options)
            lookup = nil
        }
    }

    private func show(_ result: Lookup, options: LookupOptions) {
        let notice: Confirmation
        switch result {
        case .found(let found):
            if found.source == .mail { state.mailAccess = .allowed }
            notice = copy(found.code, sender: found.sender, source: found.source, received: found.received)
        case .nothingFound:
            if options.readsMail { state.mailAccess = .allowed }
            let places = [options.readsMail ? "Mail" : nil, options.readsMessages ? "Messages" : nil].compactMap { $0 }
            notice = .nothingFound(minutes: options.lookbackMinutes, places: places.joined(separator: " and "))
        case .needsMailAccess:
            state.mailAccess = .denied
            notice = .needsMailAccess
            openSettings()
        case .needsMessagesAccess:
            state.canReadMessages = false
            notice = .needsMessagesAccess
            openSettings()
        case .messagesFailed:
            notice = .messagesFailed
        case .mailMissing:
            notice = .mailMissing
        case .noSources:
            notice = .noSources
            openSettings()
        case .failed:
            notice = .failed
        }
        confirmation.show(notice)
        announce(notice)
    }

    /// Puts a code on the clipboard and describes it for the notice.
    private func copy(_ code: String, sender: String, source: CodeSource, received: Date?) -> Confirmation {
        clipboard.copy(code, clearingAfter: preferences.clearClipboardSeconds)
        return .copied(code: preferences.showsCodeInNotice ? code : nil, sender: sender, source: source, received: received)
    }

    /// Reads the result aloud for VoiceOver users.
    private func announce(_ notice: Confirmation) {
        let text = [notice.title, notice.detail].compactMap { $0 }.joined(separator: ". ")
        NSAccessibility.post(
            element: NSApp as Any,
            notification: .announcementRequested,
            userInfo: [.announcement: text, .priority: NSAccessibilityPriorityLevel.high.rawValue]
        )
    }

    // MARK: - Authenticator

    @objc private func openAuthenticator() {
        if picker.isShown {
            picker.close()
            return
        }
        stopRecordingShortcut()
        Task {
            // "Every time" means every time, even if Settings already unlocked the vault.
            let fresh = preferences.vaultUnlockSeconds == 0
            guard await unlockVault(reason: "show your authenticator codes", fresh: fresh) else { return }
            picker.show(accounts: vault.accounts)
        }
    }

    /// How long the vault stays open after use. "Every time" still gives an open Settings
    /// window two minutes to manage accounts.
    private var unlockHoldSeconds: Int {
        if preferences.vaultUnlockSeconds > 0 { return preferences.vaultUnlockSeconds }
        return settingsWindow?.isVisible == true ? 120 : 0
    }

    /// Unlocks the vault, explaining any keychain problem. Returns whether it's unlocked.
    private func unlockVault(reason: String, fresh: Bool = false) async -> Bool {
        switch await vault.unlock(reason: reason, fresh: fresh) {
        case .unlocked:
            return true
        case .cancelled:
            return false
        case .failed:
            showAlert(
                "Couldn't Open the Authenticator",
                "Code Caller couldn't read its keychain item. If macOS asks whether Code Caller may use it, choose Always Allow."
            )
            return false
        }
    }

    private func copyAuthenticatorCode(_ account: OTPAccount) {
        // Pressing Return twice must not use up two counter-based codes.
        guard !isCopyingAuthenticatorCode else { return }
        isCopyingAuthenticatorCode = true
        Task {
            defer { isCopyingAuthenticatorCode = false }
            do {
                let code = try await vault.code(for: account)
                picker.close()
                let notice = copy(code, sender: account.displayName, source: .authenticator, received: nil)
                confirmation.show(notice)
                announce(notice)
            } catch {
                picker.close()
                showAlert("Couldn't Get the Next Code", "The keychain didn't save the account's new counter, so no code was copied. Try again.")
            }
        }
    }

    /// Saves imported accounts and reports what happened. `clearsPasteboard` removes a pasted
    /// secret from the clipboard, but only once it's safely saved.
    private func finishImport(_ result: ImportResult, fromFile: Bool, clearsPasteboard: Bool = false) async {
        // The vault may have locked while a file was being picked.
        guard await unlockVault(reason: "add authenticator accounts") else { return }
        do {
            let added = try await vault.add(result.accounts)
            if clearsPasteboard { NSPasteboard.general.clearContents() }
            vault.lock(after: unlockHoldSeconds)
            var lines = [added == 1 ? "Added 1 account." : "Added \(added) accounts."]
            let duplicates = result.accounts.count - added
            if duplicates > 0 {
                lines.append(duplicates == 1 ? "1 was already in Code Caller." : "\(duplicates) were already in Code Caller.")
            }
            if result.skipped > 0 {
                lines.append("\(result.skipped) couldn't be used, because they're Steam codes or are missing a valid setup key.")
            }
            if fromFile {
                lines.append("Delete the file you imported now. Anyone who finds it could copy your codes.")
            }
            showAlert(added > 0 ? "Accounts Imported" : "Nothing New to Add", lines.joined(separator: " "))
        } catch {
            showAlert("Couldn't Save the Accounts", "The keychain didn't accept the change. Unlock Code Caller and try again.")
        }
    }

    private func showImportError(_ error: Error) {
        let message = switch error as? ImportError {
        case .encrypted:
            "This export is password-protected. Export it again without encryption, import it, then delete the file."
        case .noQRCode:
            "There's no readable QR code in that image. Try a sharper screenshot of just the code."
        case .unrecognized:
            "Code Caller reads QR code images, otpauth links, Google Authenticator transfer codes, and exports from Apple Passwords, Bitwarden, 1Password, Aegis, 2FAS and andOTP."
        case nil:
            "Code Caller couldn't read it."
        }
        showAlert("Couldn't Import Accounts", message)
    }

    private func showAlert(_ title: String, _ message: String) {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        if let settingsWindow, settingsWindow.isVisible {
            alert.beginSheetModal(for: settingsWindow)
        } else {
            NSApp.activate()
            alert.runModal()
        }
    }

    // MARK: - Menu bar

    private func makeStatusItem() -> NSStatusItem {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = NSImage(systemSymbolName: "123.rectangle", accessibilityDescription: "Code Caller")

        let menu = NSMenu()
        let copy = NSMenuItem(title: "Copy Latest Code", action: #selector(copyLatestCode), keyEquivalent: "")
        copy.target = self
        menu.addItem(copy)
        let authenticator = NSMenuItem(title: "Authenticator…", action: #selector(openAuthenticator), keyEquivalent: "")
        authenticator.target = self
        menu.addItem(authenticator)
        menu.addItem(.separator())
        let settings = NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: ",")
        settings.target = self
        menu.addItem(settings)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit Code Caller", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        item.menu = menu

        copyItem = copy
        authenticatorItem = authenticator
        return item
    }

    private func showShortcutsInMenu() {
        for (item, shortcut) in [(copyItem, preferences.shortcut), (authenticatorItem, preferences.authenticatorShortcut)] {
            let equivalent = shortcut.menuKeyEquivalent
            item?.keyEquivalent = equivalent?.key ?? ""
            item?.keyEquivalentModifierMask = equivalent?.modifiers ?? []
        }
    }

    // MARK: - Windows

    @objc private func openSettings() {
        refreshMailAccess()
        state.canReadMessages = MessagesStore.canRead()
        settingsWindow = present(settingsWindow, title: "Code Caller Settings") {
            SettingsView(preferences: preferences, state: state, vault: vault, actions: self)
        }
    }

    private func showWelcome() {
        preferences.hasSeenWelcome = true
        welcomeWindow = present(welcomeWindow, title: "Welcome") {
            WelcomeView(preferences: preferences, state: state, actions: self) { [weak self] in
                self?.welcomeWindow?.close()
            }
        }
    }

    private func present<Content: View>(_ existing: NSWindow?, title: String, content: () -> Content) -> NSWindow {
        let window = existing ?? {
            let controller = NSHostingController(rootView: content())
            controller.sizingOptions = .preferredContentSize
            let window = NSWindow(contentViewController: controller)
            window.title = title
            window.styleMask = [.titled, .closable]
            window.isReleasedWhenClosed = false
            window.delegate = self
            window.center()
            return window
        }()
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
        return window
    }

    // MARK: - Shortcuts

    private func registerShortcuts() {
        var taken: Set<ShortcutKind> = []
        if !hotKey.register(preferences.shortcut) { taken.insert(.copyCode) }
        if !authenticatorHotKey.register(preferences.authenticatorShortcut) { taken.insert(.authenticator) }
        state.takenShortcuts = taken
        showShortcutsInMenu()
    }

    /// Returns whether the key press was used. Only presses in the Settings window itself
    /// are recorded, so typing in a sheet or another window still works normally.
    private func record(_ event: NSEvent) -> Bool {
        guard let kind = state.recordingShortcut, let settingsWindow,
              event.window === settingsWindow, settingsWindow.attachedSheet == nil else { return false }
        if event.keyCode == UInt16(kVK_Escape) {
            stopRecordingShortcut()
            return true
        }
        let other = kind == .copyCode ? preferences.authenticatorShortcut : preferences.shortcut
        guard let shortcut = Shortcut(event: event), !shortcut.conflicts(with: other) else {
            NSSound.beep()
            return true
        }
        switch kind {
        case .copyCode: preferences.shortcut = shortcut
        case .authenticator: preferences.authenticatorShortcut = shortcut
        }
        stopRecordingShortcut()
        return true
    }

    // MARK: - Mail access

    private func refreshMailAccess() {
        // Checking needs Mail to be open, and Code Caller shouldn't open it just to look.
        guard MailReader.isRunning else { return }
        Task {
            if await MailReader.isDenied() {
                state.mailAccess = .denied
            } else if state.mailAccess == .denied {
                // Turned back on in System Settings. Confirmed on the next real request.
                state.mailAccess = .unknown
            }
        }
    }
}

extension AppDelegate: NSWindowDelegate {
    func windowWillClose(_ notification: Notification) {
        guard (notification.object as? NSWindow) === settingsWindow else { return }
        stopRecordingShortcut()
        // Uses the current setting, even if it changed while Settings was open.
        vault.lock(after: preferences.vaultUnlockSeconds)
    }
}

extension AppDelegate: SettingsActions {
    func startRecordingShortcut(_ kind: ShortcutKind) {
        stopRecordingShortcut()
        // Free the current shortcuts so they can be typed again, and only listen while this window is focused.
        hotKey.unregister()
        authenticatorHotKey.unregister()
        state.recordingShortcut = kind
        shortcutRecorder = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            let used = MainActor.assumeIsolated { self?.record(event) ?? false }
            return used ? nil : event
        }
    }

    func stopRecordingShortcut() {
        if let shortcutRecorder {
            NSEvent.removeMonitor(shortcutRecorder)
            self.shortcutRecorder = nil
        }
        state.recordingShortcut = nil
        registerShortcuts()
    }

    func allowMailAccess() {
        if state.mailAccess == .denied {
            NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation")!)
            return
        }
        Task {
            do {
                try await MailReader.launchIfNeeded()
            } catch {
                state.mailAccess = .unknown
                return
            }
            state.mailAccess = await MailReader.confirmAccess()
        }
    }

    func openFullDiskAccessSettings() {
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles")!)
    }

    func setOpensAtLogin(_ enabled: Bool) {
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
        } catch {
            NSSound.beep()
        }
        state.opensAtLogin = SMAppService.mainApp.status == .enabled
    }

    func unlockVault() {
        Task {
            guard await unlockVault(reason: "manage your authenticator accounts") else { return }
            vault.lock(after: unlockHoldSeconds)
        }
    }

    func lockVault() {
        vault.lock()
    }

    func importAccounts() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.image, .json, .commaSeparatedText, .plainText, .text]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        panel.message = "Choose a QR code image, or an export from another authenticator or password manager."
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let result = try AccountImporter.accounts(from: try Data(contentsOf: url))
            Task { await finishImport(result, fromFile: true) }
        } catch {
            showImportError(error)
        }
    }

    func pasteAccounts() {
        let pasteboard = NSPasteboard.general
        do {
            let result: ImportResult
            if let text = pasteboard.string(forType: .string), !text.isEmpty {
                result = try AccountImporter.accounts(fromText: text)
            } else if let image = pasteboard.data(forType: .png) ?? pasteboard.data(forType: .tiff) {
                result = try AccountImporter.accounts(from: image)
            } else {
                throw ImportError.unrecognized
            }
            // The clipboard held a secret, so it's cleared once the accounts are saved.
            Task { await finishImport(result, fromFile: false, clearsPasteboard: true) }
        } catch {
            showImportError(error)
        }
    }

    func addAccount(_ account: OTPAccount) {
        Task { await finishImport(ImportResult(accounts: [account]), fromFile: false) }
    }

    func deleteAccount(_ account: OTPAccount) {
        Task {
            guard await unlockVault(reason: "delete an authenticator account") else { return }
            do {
                try await vault.delete(account)
                vault.lock(after: unlockHoldSeconds)
            } catch {
                showAlert("Couldn't Delete the Account", "The keychain didn't accept the change. Try again.")
            }
        }
    }
}
