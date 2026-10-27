import AppKit

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let hotKey = HotKey()
    private let clipboard = Clipboard()
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
        lookup = Task {
            let result = await CodeFinder.findLatest(lookbackMinutes: 10, checkForNewMail: true)
            show(result)
            lookup = nil
        }
    }

    private func show(_ result: Lookup) {
        switch result {
        case .found(let found):
            clipboard.copy(found.code, clearingAfter: 60)
        case .nothingFound, .needsMailAccess, .mailMissing, .failed:
            NSSound.beep()
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
