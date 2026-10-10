/// DDC/CI (VESA DDC/CI 1.1) message encoding and decoding, independent of the I2C transport.
///
/// Payloads exclude the I2C destination address (0x6E) and the host sub-address (0x51),
/// which transports pass separately.
public enum DDCPacket {
    public static let chipAddress: UInt8 = 0x37
    public static let hostAddress: UInt8 = 0x51
    public static let getVCPReplyLength = 11
    public static let capabilitiesReplyMaxLength = 38

    public enum DecodeError: Error, Equatable {
        case badLength(Int)
        case badChecksum
        case unexpectedOpcode(UInt8)
        case unexpectedVCPCode(UInt8)
        case unsupportedVCPCode(UInt8)
        case unexpectedOffset(UInt16)
    }

    public struct VCPValue: Equatable, Sendable {
        public let current: UInt16
        public let maximum: UInt16
    }

    public static func getVCP(_ code: UInt8) -> [UInt8] {
        withChecksum([0x82, 0x01, code])
    }

    public static func setVCP(_ code: UInt8, value: UInt16) -> [UInt8] {
        withChecksum([0x84, 0x03, code, UInt8(value >> 8), UInt8(value & 0xFF)])
    }

    public static func capabilities(offset: UInt16) -> [UInt8] {
        withChecksum([0x83, 0xF3, UInt8(offset >> 8), UInt8(offset & 0xFF)])
    }

    public static func decodeGetVCPReply(_ reply: [UInt8], code: UInt8) throws(DecodeError) -> VCPValue {
        guard reply.count >= getVCPReplyLength else { throw .badLength(reply.count) }
        let reply = Array(reply.prefix(getVCPReplyLength))
        guard replyChecksumIsValid(reply) else { throw .badChecksum }
        guard reply[2] == 0x02 else { throw .unexpectedOpcode(reply[2]) }
        guard reply[4] == code else { throw .unexpectedVCPCode(reply[4]) }
        guard reply[3] == 0x00 else { throw .unsupportedVCPCode(code) }
        return VCPValue(
            current: UInt16(reply[8]) << 8 | UInt16(reply[9]),
            maximum: UInt16(reply[6]) << 8 | UInt16(reply[7])
        )
    }

    /// Returns the fragment's bytes; an empty fragment means the string is complete.
    public static func decodeCapabilitiesReply(_ reply: [UInt8], offset: UInt16) throws(DecodeError) -> [UInt8] {
        guard reply.count >= 6 else { throw .badLength(reply.count) }
        let length = Int(reply[1] & 0x7F)
        guard length >= 3, reply.count >= length + 3 else { throw .badLength(reply.count) }
        let message = Array(reply.prefix(length + 3))
        guard replyChecksumIsValid(message) else { throw .badChecksum }
        guard message[2] == 0xE3 else { throw .unexpectedOpcode(message[2]) }
        let replyOffset = UInt16(message[3]) << 8 | UInt16(message[4])
        guard replyOffset == offset else { throw .unexpectedOffset(replyOffset) }
        return Array(message[5..<(length + 2)])
    }

    private static func withChecksum(_ payload: [UInt8]) -> [UInt8] {
        payload + [payload.reduce(0x6E ^ hostAddress, ^)]
    }

    /// Replies come from 0x6E to the host's virtual address 0x50.
    private static func replyChecksumIsValid(_ message: [UInt8]) -> Bool {
        message.dropLast().reduce(0x50, ^) == message.last
    }
}
