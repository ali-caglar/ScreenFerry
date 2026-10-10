import Foundation

/// A computer the user paired with, trusted by its key ID (ADR 0006).
public struct PairedPeer: Codable, Equatable, Sendable {
    /// Hex.
    public var keyID: String
    public var name: String
    public var platform: ProtocolMessage.Platform
    public var pairedAt: Date

    public init(keyID: String, name: String, platform: ProtocolMessage.Platform, pairedAt: Date) {
        self.keyID = keyID
        self.name = name
        self.platform = platform
        self.pairedAt = pairedAt
    }
}

public struct PairedPeerStore: Sendable {
    public let url: URL

    public init(url: URL) {
        self.url = url
    }

    /// `~/Library/Application Support/ScreenFerry/paired-peers.json`
    public static func standard() -> PairedPeerStore {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return PairedPeerStore(url: support.appending(path: "ScreenFerry/paired-peers.json"))
    }

    public func load() throws -> [PairedPeer] {
        guard FileManager.default.fileExists(atPath: url.path) else { return [] }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return try decoder.decode([PairedPeer].self, from: Data(contentsOf: url))
    }

    public func save(_ peers: [PairedPeer]) throws {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try encoder.encode(peers.sorted { $0.keyID < $1.keyID }).write(to: url, options: .atomic)
    }
}
