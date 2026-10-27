import AppKit

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let hotKey = HotKey()
    private var shortcut = Shortcut.standard
    private var statusItem: NSStatusItem?
    private var copyItem: NSMenuItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = makeStatusItem()
        hotKey.onPress = { [weak self] in self?.copyLatestCode() }
        hotKey.register(shortcut)
        showShortcutInMenu()
    }

    @objc private func copyLatestCode() {
        // Filled in once Code Caller can read Mail.
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
