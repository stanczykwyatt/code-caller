import AppKit
import Carbon.HIToolbox

/// A keyboard shortcut, stored as the Carbon key code and modifier mask that
/// `RegisterEventHotKey` expects.
struct Shortcut: Codable, Equatable, Sendable {
    var keyCode: UInt32
    var modifiers: UInt32
    /// The key's label as printed on the keyboard, such as "C".
    var key: String

    static let standard = Shortcut(keyCode: UInt32(kVK_ANSI_C), modifiers: UInt32(controlKey | shiftKey), key: "C")

    var displayString: String {
        var text = ""
        if modifiers & UInt32(controlKey) != 0 { text += "⌃" }
        if modifiers & UInt32(optionKey) != 0 { text += "⌥" }
        if modifiers & UInt32(shiftKey) != 0 { text += "⇧" }
        if modifiers & UInt32(cmdKey) != 0 { text += "⌘" }
        return text + key
    }

    /// The shortcut as a menu key equivalent, so menus can show it next to their item.
    /// Nil for keys like Space that menus label differently.
    var menuKeyEquivalent: (key: String, modifiers: NSEvent.ModifierFlags)? {
        guard key.count == 1 else { return nil }
        var flags: NSEvent.ModifierFlags = []
        if modifiers & UInt32(controlKey) != 0 { flags.insert(.control) }
        if modifiers & UInt32(optionKey) != 0 { flags.insert(.option) }
        if modifiers & UInt32(shiftKey) != 0 { flags.insert(.shift) }
        if modifiers & UInt32(cmdKey) != 0 { flags.insert(.command) }
        return (key.lowercased(), flags)
    }
}
