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

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = makeStatusItem()
        hotKey.onPress = { [weak self] in self?.copyLatestCode() }
        authenticatorHotKey.onPress = { [weak self] in self?.openAuthenticator() }
        picker.onCopy = { [weak self] account in self?.copyAuthenticatorCode(account) }
        picker.onClose = { [weak self] in
            guard let self else { return }
            vault.lock(after: preferences.vaultUnlockSeconds)
        }
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
        Task {
            guard await vault.unlock(reason: "show your authenticator codes") else { return }
            picker.show(accounts: vault.accounts)
        }
    }

    private func copyAuthenticatorCode(_ account: OTPAccount) {
        let notice = copy(account.code(), sender: account.displayName, source: .authenticator, received: nil)
        confirmation.show(notice)
        announce(notice)
    }

    private func finishImport(_ result: ImportResult, fromFile: Bool) {
        do {
            let added = try vault.add(result.accounts)
            var lines = [added == 1 ? "Added 1 account." : "Added \(added) accounts."]
            let duplicates = result.accounts.count - added
            if duplicates > 0 {
                lines.append(duplicates == 1 ? "1 was already in Code Caller." : "\(duplicates) were already in Code Caller.")
            }
            if result.skipped > 0 {
                lines.append("\(result.skipped) skipped because Code Caller only supports time-based codes, not counter-based or Steam ones.")
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

    private func record(_ event: NSEvent) {
        guard let kind = state.recordingShortcut else { return }
        if event.keyCode == UInt16(kVK_Escape) {
            stopRecordingShortcut()
            return
        }
        let other = kind == .copyCode ? preferences.authenticatorShortcut : preferences.shortcut
        guard let shortcut = Shortcut(event: event), shortcut != other else {
            NSSound.beep()
            return
        }
        switch kind {
        case .copyCode: preferences.shortcut = shortcut
        case .authenticator: preferences.authenticatorShortcut = shortcut
        }
        stopRecordingShortcut()
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
        if preferences.vaultUnlockSeconds == 0 {
            vault.lock()
        }
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
            MainActor.assumeIsolated { self?.record(event) }
            return nil
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
            guard await vault.unlock(reason: "manage your authenticator accounts") else { return }
            // "Every time" keeps it open until Settings closes; otherwise it locks on its timer.
            if preferences.vaultUnlockSeconds > 0 {
                vault.lock(after: preferences.vaultUnlockSeconds)
            }
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
            finishImport(try AccountImporter.accounts(from: try Data(contentsOf: url)), fromFile: true)
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
            // The clipboard held a secret, so it shouldn't stay there.
            pasteboard.clearContents()
            finishImport(result, fromFile: false)
        } catch {
            showImportError(error)
        }
    }

    func addAccount(_ account: OTPAccount) {
        finishImport(ImportResult(accounts: [account]), fromFile: false)
    }

    func deleteAccount(_ account: OTPAccount) {
        do {
            try vault.delete(account)
        } catch {
            showAlert("Couldn't Delete the Account", "The keychain didn't accept the change. Unlock Code Caller and try again.")
        }
    }
}
