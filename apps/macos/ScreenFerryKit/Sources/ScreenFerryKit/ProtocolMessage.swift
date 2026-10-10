import Foundation

/// A message between agents; see `protocol/schemas/`.
public enum ProtocolMessage: Equatable, Sendable {
    case hello(Hello)
    case pairCommit(commitment: Data)
    case pairNonce(Data)
    case pairConfirm(accepted: Bool)
    case monitors([MonitorStatus])
    case ping
    case error(ProtocolErrorCode, message: String? = nil)
    /// A type this version doesn't know; receivers ignore it.
    case unknown(type: String)

    public static let currentVersion = 0

    public struct Hello: Equatable, Sendable {
        public var keyID: Data
        public var name: String
        public var platform: Platform
        public var appVersion: String

        public init(keyID: Data, name: String, platform: Platform, appVersion: String) {
            self.keyID = keyID
            self.name = name
            self.platform = platform
            self.appVersion = appVersion
        }
    }

    public enum Platform: String, Codable, Sendable {
        case macos, windows
    }

    public struct MonitorStatus: Codable, Equatable, Sendable {
        public var identity: String
        public var name: String?
        public var attached: Bool
        public var inputCode: Int?

        public init(identity: String, name: String? = nil, attached: Bool, inputCode: Int? = nil) {
            self.identity = identity
            self.name = name
            self.attached = attached
            self.inputCode = inputCode
        }
    }

    public var type: String {
        switch self {
        case .hello: "hello"
        case .pairCommit: "pair.commit"
        case .pairNonce: "pair.nonce"
        case .pairConfirm: "pair.confirm"
        case .monitors: "monitors"
        case .ping: "ping"
        case .error: "error"
        case let .unknown(type): type
        }
    }
}

public enum ProtocolErrorCode: String, Codable, Sendable {
    case notPaired, pairingFailed, protocolVersion, badMessage
}

extension ProtocolMessage: Codable {
    private enum Keys: String, CodingKey {
        case protocolVersion, type, keyId, name, platform, appVersion, commitment, nonce, accepted,
             monitors, code, message
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: Keys.self)
        switch try c.decode(String.self, forKey: .type) {
        case "hello":
            self = .hello(Hello(
                keyID: try c.decodeHex(.keyId), name: try c.decode(String.self, forKey: .name),
                platform: try c.decode(Platform.self, forKey: .platform),
                appVersion: try c.decode(String.self, forKey: .appVersion)))
        case "pair.commit": self = .pairCommit(commitment: try c.decodeHex(.commitment))
        case "pair.nonce": self = .pairNonce(try c.decodeHex(.nonce))
        case "pair.confirm": self = .pairConfirm(accepted: try c.decode(Bool.self, forKey: .accepted))
        case "monitors": self = .monitors(try c.decode([MonitorStatus].self, forKey: .monitors))
        case "ping": self = .ping
        case "error":
            self = .error(try c.decode(ProtocolErrorCode.self, forKey: .code),
                          message: try c.decodeIfPresent(String.self, forKey: .message))
        case let type: self = .unknown(type: type)
        }
    }

    public func encode(to encoder: any Encoder) throws {
        var c = encoder.container(keyedBy: Keys.self)
        try c.encode(Self.currentVersion, forKey: .protocolVersion)
        try c.encode(type, forKey: .type)
        switch self {
        case let .hello(hello):
            try c.encode(hello.keyID.hex, forKey: .keyId)
            try c.encode(hello.name, forKey: .name)
            try c.encode(hello.platform, forKey: .platform)
            try c.encode(hello.appVersion, forKey: .appVersion)
        case let .pairCommit(commitment): try c.encode(commitment.hex, forKey: .commitment)
        case let .pairNonce(nonce): try c.encode(nonce.hex, forKey: .nonce)
        case let .pairConfirm(accepted): try c.encode(accepted, forKey: .accepted)
        case let .monitors(monitors): try c.encode(monitors, forKey: .monitors)
        case .ping, .unknown: break
        case let .error(code, message):
            try c.encode(code, forKey: .code)
            try c.encodeIfPresent(message, forKey: .message)
        }
    }
}

extension KeyedDecodingContainer {
    /// Decodes 32 bytes written as 64 lowercase hex digits.
    fileprivate func decodeHex(_ key: Key) throws -> Data {
        let text = try decode(String.self, forKey: key)
        guard let data = Data(hex: text), data.count == 32 else {
            throw DecodingError.dataCorruptedError(forKey: key, in: self, debugDescription: "Expected 64 hex digits.")
        }
        return data
    }
}

extension Data {
    public var hex: String {
        map { String(format: "%02x", $0) }.joined()
    }

    public init?(hex: String) {
        guard hex.count.isMultiple(of: 2), hex.allSatisfy({ $0.isHexDigit && !$0.isUppercase }) else { return nil }
        var bytes = [UInt8]()
        var index = hex.startIndex
        while index < hex.endIndex {
            let next = hex.index(index, offsetBy: 2)
            guard let byte = UInt8(hex[index..<next], radix: 16) else { return nil }
            bytes.append(byte)
            index = next
        }
        self.init(bytes)
    }
}

/// Splits a byte stream into messages: each is a 4-byte big-endian length and that many bytes of JSON.
public struct MessageFramer: Sendable {
    public static let maxLength = 65_536

    public enum FramingError: Error, Equatable {
        case tooLong(Int)
    }

    private var buffer = Data()

    public init() {}

    public static func frame(_ message: ProtocolMessage) throws -> Data {
        let json = try JSONEncoder().encode(message)
        guard json.count <= maxLength else { throw FramingError.tooLong(json.count) }
        return withUnsafeBytes(of: UInt32(json.count).bigEndian) { Data($0) } + json
    }

    /// Adds received bytes and returns the complete messages' JSON, in order.
    public mutating func append(_ bytes: Data) throws -> [Data] {
        buffer.append(bytes)
        var frames = [Data]()
        while buffer.count >= 4 {
            let length = buffer.prefix(4).reduce(0) { $0 << 8 | Int($1) }
            guard length <= Self.maxLength else { throw FramingError.tooLong(length) }
            guard buffer.count >= 4 + length else { break }
            frames.append(Data(buffer.dropFirst(4).prefix(length)))
            buffer = Data(buffer.dropFirst(4 + length))
        }
        return frames
    }
}
