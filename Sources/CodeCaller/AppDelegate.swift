import AppKit
import Carbon.HIToolbox
import ServiceManagement
import SwiftUI

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let preferences = Preferences()
    private let state = AppState()
    private let hotKey = HotKey()
    private let clipboard = Clipboard()
    private lazy var confirmation = ConfirmationPanel()
    private var statusItem: NSStatusItem?
    private var copyItem: NSMenuItem?
    private var settingsWindow: NSWindow?
    private var welcomeWindow: NSWindow?
    private var lookup: Task<Void, Never>?
    private var shortcutRecorder: Any?

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = makeStatusItem()
        hotKey.onPress = { [weak self] in self?.copyLatestCode() }
        registerShortcut()
        if !preferences.hasSeenWelcome {
            showWelcome()
        }
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        // Picks up a change made in System Settings while Code Caller was in the background.
        refreshMailAccess()
    }

    func applicationWillTerminate(_ notification: Notification) {
        clipboard.clearIfUnchanged()
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
        let minutes = preferences.lookbackMinutes
        let checksForNewMail = preferences.checksForNewMail
        lookup = Task {
            let result = await CodeFinder.findLatest(lookbackMinutes: minutes, checkForNewMail: checksForNewMail)
            progress.cancel()
            show(result, lookbackMinutes: minutes)
            lookup = nil
        }
    }

    private func show(_ result: Lookup, lookbackMinutes: Int) {
        let notice: Confirmation
        switch result {
        case .found(let found):
            state.mailAccess = .allowed
            clipboard.copy(found.code, clearingAfter: preferences.clearClipboardSeconds)
            notice = .copied(code: preferences.showsCodeInNotice ? found.code : nil, sender: found.sender, received: found.received)
        case .nothingFound:
            state.mailAccess = .allowed
            notice = .nothingFound(minutes: lookbackMinutes)
        case .needsMailAccess:
            state.mailAccess = .denied
            notice = .needsMailAccess
            openSettings()
        case .mailMissing:
            notice = .mailMissing
        case .failed:
            notice = .failed
        }
        confirmation.show(notice)
        announce(notice)
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

    // MARK: - Menu bar

    private func makeStatusItem() -> NSStatusItem {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = NSImage(systemSymbolName: "123.rectangle", accessibilityDescription: "Code Caller")

        let menu = NSMenu()
        let copy = NSMenuItem(title: "Copy Latest Code", action: #selector(copyLatestCode), keyEquivalent: "")
        copy.target = self
        menu.addItem(copy)
        menu.addItem(.separator())
        let settings = NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: ",")
        settings.target = self
        menu.addItem(settings)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit Code Caller", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        item.menu = menu

        copyItem = copy
        return item
    }

    private func showShortcutInMenu() {
        let equivalent = preferences.shortcut.menuKeyEquivalent
        copyItem?.keyEquivalent = equivalent?.key ?? ""
        copyItem?.keyEquivalentModifierMask = equivalent?.modifiers ?? []
    }

    // MARK: - Windows

    @objc private func openSettings() {
        refreshMailAccess()
        settingsWindow = present(settingsWindow, title: "Code Caller Settings") {
            SettingsView(preferences: preferences, state: state, actions: self)
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
            window.center()
            return window
        }()
        NSApp.activate()
        window.makeKeyAndOrderFront(nil)
        return window
    }

    // MARK: - Shortcut

    private func registerShortcut() {
        state.shortcutIsTaken = !hotKey.register(preferences.shortcut)
        showShortcutInMenu()
    }

    private func record(_ event: NSEvent) {
        if event.keyCode == UInt16(kVK_Escape) {
            stopRecordingShortcut()
        } else if let shortcut = Shortcut(event: event) {
            preferences.shortcut = shortcut
            stopRecordingShortcut()
        } else {
            NSSound.beep()
        }
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

extension AppDelegate: SettingsActions {
    func startRecordingShortcut() {
        guard shortcutRecorder == nil else { return }
        // Free the current shortcut so it can be typed again, and only listen while this window is focused.
        hotKey.unregister()
        state.isRecordingShortcut = true
        shortcutRecorder = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            MainActor.assumeIsolated { self?.record(event) }
            return nil
        }
    }

    func stopRecordingShortcut() {
        guard let shortcutRecorder else { return }
        NSEvent.removeMonitor(shortcutRecorder)
        self.shortcutRecorder = nil
        state.isRecordingShortcut = false
        registerShortcut()
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
}
