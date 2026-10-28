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
    static let authenticator = Shortcut(keyCode: UInt32(kVK_ANSI_A), modifiers: UInt32(controlKey | shiftKey), key: "A")

    init(keyCode: UInt32, modifiers: UInt32, key: String) {
        self.keyCode = keyCode
        self.modifiers = modifiers
        self.key = key
    }

    /// Whether both use the same physical key and modifiers. The printed label is ignored,
    /// since the same key is labeled differently on other keyboard layouts.
    func conflicts(with other: Shortcut) -> Bool {
        keyCode == other.keyCode && modifiers == other.modifiers
    }

    /// Reads a shortcut from a key press. Needs ⌃ or ⌥, or ⌘ together with ⇧, so plain typing
    /// and everyday commands like ⌘C, ⌘V and ⌘Q are never taken over in every app.
    init?(event: NSEvent) {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let isSafe = flags.contains(.control) || flags.contains(.option) || flags.isSuperset(of: [.command, .shift])
        guard isSafe, let key = Self.label(for: event) else {
            return nil
        }
        var modifiers = 0
        if flags.contains(.control) { modifiers |= controlKey }
        if flags.contains(.option) { modifiers |= optionKey }
        if flags.contains(.shift) { modifiers |= shiftKey }
        if flags.contains(.command) { modifiers |= cmdKey }
        self.init(keyCode: UInt32(event.keyCode), modifiers: UInt32(modifiers), key: key)
    }

    private static func label(for event: NSEvent) -> String? {
        switch Int(event.keyCode) {
        case kVK_Space: return "Space"
        case kVK_Return: return "↩"
        case kVK_Tab: return "⇥"
        case kVK_Delete: return "⌫"
        case kVK_LeftArrow: return "←"
        case kVK_RightArrow: return "→"
        case kVK_UpArrow: return "↑"
        case kVK_DownArrow: return "↓"
        default: break
        }
        // The key's own label, without Shift or Option changing it into another character.
        guard let scalar = event.characters(byApplyingModifiers: [])?.unicodeScalars.first else { return nil }
        if (0xF704...0xF70F).contains(scalar.value) {
            return "F\(scalar.value - 0xF703)"
        }
        guard scalar.properties.isAlphabetic || scalar.properties.numericType != nil || scalar.properties.generalCategory.isPunctuationOrSymbol else {
            return nil
        }
        return String(scalar).uppercased()
    }

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
        guard key.count == 1, key.first?.isASCII == true else { return nil }
        var flags: NSEvent.ModifierFlags = []
        if modifiers & UInt32(controlKey) != 0 { flags.insert(.control) }
        if modifiers & UInt32(optionKey) != 0 { flags.insert(.option) }
        if modifiers & UInt32(shiftKey) != 0 { flags.insert(.shift) }
        if modifiers & UInt32(cmdKey) != 0 { flags.insert(.command) }
        return (key.lowercased(), flags)
    }
}

private extension Unicode.GeneralCategory {
    var isPunctuationOrSymbol: Bool {
        switch self {
        case .connectorPunctuation, .dashPunctuation, .openPunctuation, .closePunctuation,
             .initialPunctuation, .finalPunctuation, .otherPunctuation,
             .mathSymbol, .currencySymbol, .modifierSymbol, .otherSymbol:
            true
        default:
            false
        }
    }
}
