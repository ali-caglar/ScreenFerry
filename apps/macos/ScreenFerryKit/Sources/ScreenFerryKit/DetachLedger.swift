import Foundation

/// Monitors this machine has released, persisted so they can be re-attached after a restart.
public struct DetachLedger: Codable, Equatable, Sendable {
    public struct Entry: Codable, Equatable, Sendable {
        public var detachedAt: Date
        /// Whatever the platform needs to re-attach: a display id on macOS, a layout on Windows.
        public var platformData: String?

        public init(detachedAt: Date, platformData: String? = nil) {
            self.detachedAt = detachedAt
            self.platformData = platformData
        }
    }

    public var version = 1
    /// Keyed by monitor identity.
    public var monitors: [String: Entry] = [:]

    public init(monitors: [String: Entry] = [:]) {
        self.monitors = monitors
    }
}

public struct DetachLedgerStore: Sendable {
    public let url: URL

    public init(url: URL) {
        self.url = url
    }

    /// `~/Library/Application Support/ScreenFerry/detached-displays.json`
    public static func standard() -> DetachLedgerStore {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return DetachLedgerStore(url: support.appending(path: "ScreenFerry/detached-displays.json"))
    }

    public func load() throws -> DetachLedger {
        guard FileManager.default.fileExists(atPath: url.path) else { return DetachLedger() }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return try decoder.decode(DetachLedger.self, from: Data(contentsOf: url))
    }

    public func save(_ ledger: DetachLedger) throws {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try encoder.encode(ledger).write(to: url, options: .atomic)
    }

    public func update(_ change: (inout DetachLedger) throws -> Void) throws {
        var ledger = try load()
        try change(&ledger)
        try save(ledger)
    }
}

/// Monitors that follow a newly appearing source only notice it after it was gone for a while (ADR 0005).
public enum MinimumAbsence {
    public static let defaultSeconds: TimeInterval = 25

    /// How long to wait before attaching a monitor released at `detachedAt`.
    public static func remainingWait(since detachedAt: Date?, now: Date, minimum: TimeInterval) -> TimeInterval {
        guard let detachedAt, minimum > 0 else { return 0 }
        return max(0, minimum - now.timeIntervalSince(detachedAt))
    }
}
