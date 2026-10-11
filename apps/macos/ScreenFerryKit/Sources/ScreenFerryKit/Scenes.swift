import Foundation

/// Monitors and the computers that should have them (ADR 0007).
public struct Scene: Codable, Equatable, Identifiable, Sendable {
    public var id: String
    public var name: String
    /// Monitor identity → owner key ID (hex).
    public var owners: [String: String]

    public init(id: String = UUID().uuidString, name: String, owners: [String: String]) {
        self.id = id
        self.name = name
        self.owners = owners
    }
}

/// A Codable value in a JSON file under `~/Library/Application Support/ScreenFerry/`.
public struct JSONFileStore<Value: Codable & Sendable>: Sendable {
    public let url: URL

    public init(url: URL) {
        self.url = url
    }

    public static func standard(_ name: String) -> JSONFileStore {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return JSONFileStore(url: support.appending(path: "ScreenFerry/\(name)"))
    }

    public func load(default value: Value) throws -> Value {
        guard FileManager.default.fileExists(atPath: url.path) else { return value }
        return try JSONDecoder().decode(Value.self, from: Data(contentsOf: url))
    }

    public func save(_ value: Value) throws {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try encoder.encode(value).write(to: url, options: .atomic)
    }
}

extension JSONFileStore where Value == [Scene] {
    /// The user's scenes; built-in ones aren't stored.
    public static func scenes() -> Self { standard("scenes.json") }
}

extension JSONFileStore where Value == [String: Int] {
    /// Monitor identity → this computer's VCP `0x60` value.
    public static func inputCodes() -> Self { standard("input-codes.json") }
}
