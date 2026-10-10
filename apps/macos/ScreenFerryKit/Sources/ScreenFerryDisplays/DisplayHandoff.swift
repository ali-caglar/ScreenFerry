import CoreGraphics
import Foundation
import ScreenFerryDDC
import ScreenFerryKit

public enum DisplayHandoffError: Error, CustomStringConvertible {
    case unknownMonitor(String)
    case attachFailed(String)

    public var description: String {
        switch self {
        case let .unknownMonitor(identity): "No connected monitor with identity \(identity)."
        case let .attachFailed(identity): "Could not attach \(identity); it stays released. Try again, or replug its cable."
        }
    }
}

/// Releases and takes monitors by identity, remembering what this Mac released (ADR 0005).
public struct DisplayHandoff: Sendable {
    public let store: DetachLedgerStore

    public init(store: DetachLedgerStore = .standard()) {
        self.store = store
    }

    public func released() throws -> [String: DetachLedger.Entry] {
        try store.load().monitors
    }

    /// Identities of the external monitors macOS knows, attached or not.
    public func knownIdentities() -> [String] {
        ((try? ExternalDisplay.all()) ?? []).compactMap(\.edid?.identity)
    }

    /// Detaches the monitor and records when, so `take` can honor the minimum absence.
    public func release(_ identity: String, now: Date = .now) throws {
        let edid = try edid(for: identity)
        let active = DisplayControl.displays().filter { $0.isActive && Self.matches(edid, $0) }
        let previous = try store.load().monitors[identity]
        guard !active.isEmpty else {
            if previous == nil { try record(identity, DetachLedger.Entry(detachedAt: now)) }
            return
        }
        try record(identity, DetachLedger.Entry(detachedAt: now, platformData: String(active[0].id)))
        do {
            for display in active { try DisplayControl.setEnabled(display.id, false) }
        } catch {
            try? store.update { $0.monitors[identity] = previous }
            throw error
        }
    }

    /// Waits out the minimum absence, attaches the monitor and checks that it is active.
    /// Returns the seconds waited.
    @discardableResult
    public func take(
        _ identity: String,
        minimumAbsence: TimeInterval = MinimumAbsence.defaultSeconds,
        now: () -> Date = { .now },
        sleep: (TimeInterval) -> Void = { Thread.sleep(forTimeInterval: $0) }
    ) throws -> TimeInterval {
        let edid = try edid(for: identity)
        let entry = try store.load().monitors[identity]
        let wait = MinimumAbsence.remainingWait(since: entry?.detachedAt, now: now(), minimum: minimumAbsence)
        if wait > 0 { sleep(wait) }

        if !isActive(edid) {
            let known = entry?.platformData.flatMap(CGDirectDisplayID.init).map { [$0] } ?? []
            let candidates = known + DisplayControl.allDisplays().filter { Self.matches(edid, $0) }.map(\.id)
            var tried = Set<CGDirectDisplayID>()
            for id in candidates where tried.insert(id).inserted {
                try? DisplayControl.setEnabled(id, true)
                if isActive(edid) { break }
            }
        }
        guard isActive(edid) else { throw DisplayHandoffError.attachFailed(identity) }
        try store.update { $0.monitors[identity] = nil }
        return wait
    }

    /// Detaches released monitors that came back, e.g. after a restart or replug. Returns their identities.
    @discardableResult
    public func reconcile(now: Date = .now) throws -> [String] {
        var reverted: [String] = []
        for identity in try store.load().monitors.keys.sorted() {
            guard let edid = try? edid(for: identity), isActive(edid) else { continue }
            try release(identity, now: now)
            reverted.append(identity)
        }
        return reverted
    }

    private func edid(for identity: String) throws -> EDID {
        guard let edid = ((try? ExternalDisplay.all()) ?? []).compactMap(\.edid).first(where: { $0.identity == identity })
        else { throw DisplayHandoffError.unknownMonitor(identity) }
        return edid
    }

    private func record(_ identity: String, _ entry: DetachLedger.Entry) throws {
        try store.update { $0.monitors[identity] = entry }
    }

    private func isActive(_ edid: EDID) -> Bool {
        DisplayControl.displays().contains { $0.isActive && Self.matches(edid, $0) }
    }

    public static func matches(_ edid: EDID, _ display: DisplayInfo) -> Bool {
        let vendor = UInt32(edid.baseBlock[8]) << 8 | UInt32(edid.baseBlock[9])
        return vendor == display.vendor && UInt32(edid.productCode) == display.model
            && (display.serial == 0 || edid.serialNumber == display.serial)
    }
}
