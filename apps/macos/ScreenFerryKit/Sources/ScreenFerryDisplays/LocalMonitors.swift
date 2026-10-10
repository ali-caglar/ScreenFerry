import ScreenFerryDDC
import ScreenFerryKit

public enum LocalMonitors {
    /// External monitors this Mac sees, attached or detached, as reported to peers.
    public static func current() -> [ProtocolMessage.MonitorStatus] {
        let edids = ((try? ExternalDisplay.all()) ?? []).compactMap(\.edid)
        var monitors: [String: ProtocolMessage.MonitorStatus] = [:]
        for display in DisplayControl.allDisplays() where !display.isBuiltin {
            guard let edid = edids.first(where: { DisplayHandoff.matches($0, display) }) else { continue }
            // One monitor can have several display ids; it is attached if any of them is.
            monitors[edid.identity, default: .init(identity: edid.identity, name: edid.name, attached: false)].attached =
                monitors[edid.identity]?.attached == true || display.isActive
        }
        return monitors.values.sorted { $0.identity < $1.identity }
    }
}
