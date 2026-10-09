import Foundation

/// Fields shared by every protocol message; see `protocol/schemas/envelope.schema.json`.
public struct ProtocolEnvelope: Codable, Equatable, Sendable {
    public var protocolVersion: Int
    public var type: String

    public init(protocolVersion: Int, type: String) {
        self.protocolVersion = protocolVersion
        self.type = type
    }
}
