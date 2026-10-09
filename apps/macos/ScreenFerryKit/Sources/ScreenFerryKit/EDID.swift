import CryptoKit
import Foundation

/// The fields of an EDID base block that ScreenFerry uses.
public struct EDID: Equatable, Sendable {
    public enum ParseError: Error, Equatable {
        case tooShort(Int)
        case badHeader
        case badChecksum
    }

    public static let blockSize = 128
    private static let header: [UInt8] = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00]

    /// The 128-byte base block, without extension blocks.
    public let baseBlock: [UInt8]
    /// Three-letter PNP ID, e.g. `SAM`.
    public let manufacturerID: String
    public let productCode: UInt16
    /// 0 when the monitor doesn't set it.
    public let serialNumber: UInt32
    /// Display descriptor `0xFF`, if present.
    public let serialString: String?
    /// Display descriptor `0xFC`, if present.
    public let name: String?
    public let manufactureYear: Int

    public init(_ data: some Collection<UInt8>) throws(ParseError) {
        let bytes = Array(data.prefix(Self.blockSize))
        guard bytes.count == Self.blockSize else { throw .tooShort(bytes.count) }
        guard Array(bytes[0..<8]) == Self.header else { throw .badHeader }
        guard bytes.reduce(0, { $0 &+ $1 }) == 0 else { throw .badChecksum }

        baseBlock = bytes
        let packed = UInt16(bytes[8]) << 8 | UInt16(bytes[9])
        manufacturerID = String(
            [10, 5, 0].map { Character(UnicodeScalar(UInt8((packed >> $0) & 0x1F) + 64)) }
        )
        productCode = UInt16(bytes[10]) | UInt16(bytes[11]) << 8
        serialNumber = UInt32(bytes[12]) | UInt32(bytes[13]) << 8 | UInt32(bytes[14]) << 16 | UInt32(bytes[15]) << 24
        manufactureYear = 1990 + Int(bytes[17])

        var serialString: String?
        var name: String?
        for offset in stride(from: 54, through: 108, by: 18) {
            let descriptor = bytes[offset..<offset + 18]
            guard descriptor.prefix(3).allSatisfy({ $0 == 0 }) else { continue }
            let text = Self.descriptorText(descriptor.dropFirst(5))
            switch descriptor[descriptor.startIndex + 3] {
            case 0xFF: serialString = text
            case 0xFC: name = text
            default: break
            }
        }
        self.serialString = serialString
        self.name = name
    }

    /// Stable identity shared by every computer that sees this monitor; see `protocol/README.md`.
    public var identity: String {
        let prefix = "\(manufacturerID)-\(String(format: "%04X", productCode))"
        if let serialString { return "\(prefix)-\(serialString)" }
        if serialNumber != 0 { return "\(prefix)-\(serialNumber)" }
        let digest = SHA256.hash(data: Data(baseBlock))
        return "edid-" + digest.prefix(8).map { String(format: "%02x", $0) }.joined()
    }

    private static func descriptorText(_ bytes: ArraySlice<UInt8>) -> String? {
        let text = String(decoding: bytes.prefix { $0 != 0x0A && $0 != 0x00 }, as: UTF8.self)
            .trimmingCharacters(in: .whitespaces)
        return text.isEmpty ? nil : text
    }
}
