import AppKit

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let hotKey = HotKey()
    private let clipboard = Clipboard()
    private lazy var confirmation = ConfirmationPanel()
    private var shortcut = Shortcut.standard
    private var statusItem: NSStatusItem?
    private var copyItem: NSMenuItem?
    private var lookup: Task<Void, Never>?

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = makeStatusItem()
        hotKey.onPress = { [weak self] in self?.copyLatestCode() }
        hotKey.register(shortcut)
        showShortcutInMenu()
    }

    func applicationWillTerminate(_ notification: Notification) {
        clipboard.clearIfUnchanged()
    }

    @objc private func copyLatestCode() {
        // Pressing the shortcut again while a search is running does nothing.
        guard lookup == nil else { return }
        // Only show progress if the search takes long enough to notice.
        let progress = Task {
            try? await Task.sleep(for: .milliseconds(400))
            if !Task.isCancelled { confirmation.show(.looking) }
        }
        lookup = Task {
            let result = await CodeFinder.findLatest(lookbackMinutes: 10, checkForNewMail: true)
            progress.cancel()
            show(result)
            lookup = nil
        }
    }

    private func show(_ result: Lookup) {
        let notice: Confirmation
        switch result {
        case .found(let found):
            clipboard.copy(found.code, clearingAfter: 60)
            notice = .copied(code: found.code, sender: found.sender, received: found.received)
        case .nothingFound:
            notice = .nothingFound(minutes: 10)
        case .needsMailAccess:
            notice = .needsMailAccess
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
        menu.addItem(NSMenuItem(title: "Quit Code Caller", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        item.menu = menu

        copyItem = copy
        return item
    }

    private func showShortcutInMenu() {
        let equivalent = shortcut.menuKeyEquivalent
        copyItem?.keyEquivalent = equivalent?.key ?? ""
        copyItem?.keyEquivalentModifierMask = equivalent?.modifiers ?? []
    }
}
