import AppKit

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem?
    private var copyItem: NSMenuItem?

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = makeStatusItem()
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
}
