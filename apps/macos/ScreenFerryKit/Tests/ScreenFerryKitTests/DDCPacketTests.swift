import Testing
@testable import ScreenFerryKit

struct DDCPacketTests {
    static func reply(_ body: [UInt8]) -> [UInt8] {
        body + [body.reduce(0x50, ^)]
    }

    @Test func encodesRequests() {
        #expect(DDCPacket.getVCP(0x10) == [0x82, 0x01, 0x10, 0xAC])
        #expect(DDCPacket.setVCP(0x60, value: 0x0F) == [0x84, 0x03, 0x60, 0x00, 0x0F, 0xD7])
        #expect(DDCPacket.capabilities(offset: 0x0120) == [0x83, 0xF3, 0x01, 0x20, 0x3F ^ 0x83 ^ 0xF3 ^ 0x01 ^ 0x20])
    }

    @Test func decodesGetVCPReply() throws {
        let reply = Self.reply([0x6E, 0x88, 0x02, 0x00, 0x60, 0x00, 0x00, 0x12, 0x00, 0x0F])
        let value = try DDCPacket.decodeGetVCPReply(reply + [0, 0], code: 0x60)
        #expect(value == .init(current: 0x0F, maximum: 0x12))
    }

    @Test func rejectsBadGetVCPReplies() {
        let good = Self.reply([0x6E, 0x88, 0x02, 0x00, 0x10, 0x00, 0x00, 0x64, 0x00, 0x32])
        var corrupt = good
        corrupt[9] ^= 0xFF
        #expect(throws: DDCPacket.DecodeError.badChecksum) { try DDCPacket.decodeGetVCPReply(corrupt, code: 0x10) }
        #expect(throws: DDCPacket.DecodeError.unexpectedVCPCode(0x10)) { try DDCPacket.decodeGetVCPReply(good, code: 0x60) }
        #expect(throws: DDCPacket.DecodeError.badLength(3)) { try DDCPacket.decodeGetVCPReply([1, 2, 3], code: 0x10) }
        let unsupported = Self.reply([0x6E, 0x88, 0x02, 0x01, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00])
        #expect(throws: DDCPacket.DecodeError.unsupportedVCPCode(0x10)) { try DDCPacket.decodeGetVCPReply(unsupported, code: 0x10) }
    }

    @Test func decodesCapabilitiesFragments() throws {
        let text = Array("(prot(monitor)".utf8)
        let fragment = Self.reply([0x6E, 0x80 | UInt8(3 + text.count), 0xE3, 0x00, 0x20] + text)
        #expect(try DDCPacket.decodeCapabilitiesReply(fragment + [0, 0, 0], offset: 0x20) == text)

        let end = Self.reply([0x6E, 0x83, 0xE3, 0x00, 0x40])
        #expect(try DDCPacket.decodeCapabilitiesReply(end, offset: 0x40).isEmpty)
        #expect(throws: DDCPacket.DecodeError.unexpectedOffset(0x40)) { try DDCPacket.decodeCapabilitiesReply(end, offset: 0) }
    }
}
