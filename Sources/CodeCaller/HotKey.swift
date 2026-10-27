import Carbon.HIToolbox

/// A system-wide keyboard shortcut.
///
/// Uses the Carbon hot key API, which only reports the shortcuts that were
/// registered. Code Caller never needs Accessibility or Input Monitoring
/// permission and never sees anything else you type.
@MainActor
final class HotKey {
    var onPress: () -> Void = {}
    /// Tells this shortcut apart from the app's others, since every handler hears every hot key.
    nonisolated let id: UInt32
    private var hotKeyRef: EventHotKeyRef?
    private var handlerRef: EventHandlerRef?

    init(id: UInt32) {
        self.id = id
        var pressed = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, context in
            guard let event, let context else { return OSStatus(eventNotHandledErr) }
            var pressedID = EventHotKeyID()
            let status = GetEventParameter(
                event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                nil, MemoryLayout<EventHotKeyID>.size, nil, &pressedID
            )
            let hotKey = Unmanaged<HotKey>.fromOpaque(context).takeUnretainedValue()
            guard status == noErr, pressedID.id == hotKey.id else { return OSStatus(eventNotHandledErr) }
            // Carbon delivers hot key events on the main thread.
            MainActor.assumeIsolated { hotKey.onPress() }
            return noErr
        }, 1, &pressed, Unmanaged.passUnretained(self).toOpaque(), &handlerRef)
    }

    /// Returns false if another app already uses the shortcut.
    @discardableResult
    func register(_ shortcut: Shortcut) -> Bool {
        unregister()
        let hotKeyID = EventHotKeyID(signature: OSType(0x4343_4C52), id: id)  // "CCLR"
        let status = RegisterEventHotKey(shortcut.keyCode, shortcut.modifiers, hotKeyID, GetApplicationEventTarget(), 0, &hotKeyRef)
        return status == noErr
    }

    func unregister() {
        guard let hotKeyRef else { return }
        UnregisterEventHotKey(hotKeyRef)
        self.hotKeyRef = nil
    }
}
