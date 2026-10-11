/// Finds a computer's own monitor for one reported by another computer (ADR 0007,
/// `protocol/test-vectors/monitor-matching.json`).
public enum MonitorMatch {
    public static func find(_ reference: ProtocolMessage.MonitorStatus, in candidates: [ProtocolMessage.MonitorStatus]) -> ProtocolMessage.MonitorStatus? {
        if let exact = candidates.first(where: { $0.identity == reference.identity }) { return exact }
        guard let key = looseKey(reference) else { return nil }
        let matches = candidates.filter { looseKey($0) == key }
        return matches.count == 1 ? matches[0] : nil
    }

    /// Manufacturer, serial part and name; nil for hash identities and unnamed monitors.
    static func looseKey(_ monitor: ProtocolMessage.MonitorStatus) -> String? {
        guard let name = monitor.name, !name.isEmpty else { return nil }
        let parts = monitor.identity.split(separator: "-", maxSplits: 2, omittingEmptySubsequences: false)
        guard parts.count == 3, parts[0].count == 3, parts[0].allSatisfy({ $0.isASCII && $0.isUppercase }),
              parts[1].count == 4, parts[1].allSatisfy(\.isHexDigit), !parts[2].isEmpty
        else { return nil }
        return "\(parts[0])\n\(parts[2])\n\(name)"
    }
}
