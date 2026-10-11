import ScreenFerryDDC
import ScreenFerryKit

public enum LocalMonitors {
    public struct Scan: Sendable {
        /// External monitors this Mac sees, attached or detached, as reported to peers.
        public var monitors: [ProtocolMessage.MonitorStatus]
        /// Identities shared by two connected monitors; they are left out (ADR 0007).
        public var ambiguous: [String]
    }

    public static func scan() -> Scan {
        let edids = ((try? ExternalDisplay.all()) ?? []).compactMap(\.edid)
        let counts = Dictionary(edids.map { ($0.identity, 1) }, uniquingKeysWith: +)
        var monitors: [String: ProtocolMessage.MonitorStatus] = [:]
        for display in DisplayControl.allDisplays() where !display.isBuiltin {
            guard let edid = edids.first(where: { DisplayHandoff.matches($0, display) }), counts[edid.identity] == 1 else { continue }
            // One monitor can have several display ids; it is attached if any of them is.
            monitors[edid.identity, default: .init(identity: edid.identity, name: edid.name, attached: false)].attached =
                monitors[edid.identity]?.attached == true || display.isActive
        }
        return Scan(monitors: monitors.values.sorted { $0.identity < $1.identity },
                    ambiguous: counts.filter { $0.value > 1 }.keys.sorted())
    }
}

/// VCP `0x60` of a monitor by identity.
public enum InputSource {
    public static func read(_ identity: String) throws -> Int {
        Int(try display(identity).getVCP(0x60).current & 0xFF)
    }

    public static func set(_ identity: String, to code: Int) throws {
        try display(identity).setVCP(0x60, value: UInt16(code))
    }

    private static func display(_ identity: String) throws -> ExternalDisplay {
        guard let display = try ExternalDisplay.all().first(where: { $0.edid?.identity == identity }) else {
            throw DisplayHandoffError.unknownMonitor(identity)
        }
        return display
    }
}
