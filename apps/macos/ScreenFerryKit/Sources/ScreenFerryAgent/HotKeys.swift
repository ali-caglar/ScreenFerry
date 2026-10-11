import Carbon.HIToolbox

/// ⌃⌥⌘1 … ⌃⌥⌘9, system-wide.
@MainActor
final class HotKeys {
    private static let keyCodes = [kVK_ANSI_1, kVK_ANSI_2, kVK_ANSI_3, kVK_ANSI_4, kVK_ANSI_5, kVK_ANSI_6, kVK_ANSI_7, kVK_ANSI_8, kVK_ANSI_9]
    private static let signature: OSType = 0x5346_4552 // "SFER"
    private static var action: ((Int) -> Void)?

    private var refs: [EventHotKeyRef] = []
    private var handler: EventHandlerRef?

    init(action: @escaping (Int) -> Void) {
        Self.action = action
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, event, _ in
            var id = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                           nil, MemoryLayout<EventHotKeyID>.size, nil, &id)
            guard status == noErr, id.signature == HotKeys.signature else { return OSStatus(eventNotHandledErr) }
            let index = Int(id.id)
            Task { @MainActor in HotKeys.action?(index) }
            return noErr
        }, 1, &spec, nil, &handler)
    }

    /// Registers the hotkeys for scenes 1 … `count` (at most nine).
    func register(count: Int) {
        for ref in refs { UnregisterEventHotKey(ref) }
        refs = []
        for (index, keyCode) in Self.keyCodes.prefix(count).enumerated() {
            var ref: EventHotKeyRef?
            let id = EventHotKeyID(signature: Self.signature, id: UInt32(index + 1))
            if RegisterEventHotKey(UInt32(keyCode), UInt32(controlKey | optionKey | cmdKey), id, GetApplicationEventTarget(), 0, &ref) == noErr,
               let ref {
                refs.append(ref)
            }
        }
    }
}
