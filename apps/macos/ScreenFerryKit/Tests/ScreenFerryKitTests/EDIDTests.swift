import Testing
@testable import ScreenFerryKit

struct EDIDTests {
    /// Builds a valid base block; descriptors are (tag, text) pairs.
    static func makeEDID(
        manufacturer: String = "SAM",
        product: UInt16 = 0xE030,
        serial: UInt32 = 0,
        descriptors: [(UInt8, String)] = []
    ) -> [UInt8] {
        var bytes = [UInt8](repeating: 0, count: 128)
        bytes[0..<8] = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00]
        let letters = manufacturer.unicodeScalars.map { UInt16($0.value - 64) }
        let packed = letters[0] << 10 | letters[1] << 5 | letters[2]
        bytes[8] = UInt8(packed >> 8)
        bytes[9] = UInt8(packed & 0xFF)
        bytes[10] = UInt8(product & 0xFF)
        bytes[11] = UInt8(product >> 8)
        for i in 0..<4 { bytes[12 + i] = UInt8((serial >> (8 * UInt32(i))) & 0xFF) }
        bytes[17] = 34
        for (index, (tag, text)) in descriptors.enumerated() {
            let offset = 54 + index * 18
            bytes[offset + 3] = tag
            var field = Array(text.utf8.prefix(13))
            if field.count < 13 { field.append(0x0A) }
            while field.count < 13 { field.append(0x20) }
            bytes[(offset + 5)..<(offset + 18)] = field[...]
        }
        bytes[127] = 0 &- bytes[0..<127].reduce(0, &+)
        return bytes
    }

    @Test func parsesIdentityFields() throws {
        let edid = try EDID(Self.makeEDID(serial: 0x0100_0E00, descriptors: [(0xFC, "Odyssey G80SD"), (0xFF, "H1AK500000")]))
        #expect(edid.manufacturerID == "SAM")
        #expect(edid.productCode == 0xE030)
        #expect(edid.serialNumber == 0x0100_0E00)
        #expect(edid.name == "Odyssey G80SD")
        #expect(edid.serialString == "H1AK500000")
        #expect(edid.manufactureYear == 2024)
    }

    @Test func identityPrefersSerialString() throws {
        let edid = try EDID(Self.makeEDID(serial: 42, descriptors: [(0xFF, "ABC123")]))
        #expect(edid.identity == "SAM-E030-ABC123")
    }

    @Test func identityFallsBackToNumericSerial() throws {
        let edid = try EDID(Self.makeEDID(manufacturer: "AUS", product: 0x2703, serial: 153_957))
        #expect(edid.identity == "AUS-2703-153957")
    }

    @Test func identityFallsBackToHashWithoutSerial() throws {
        let identity = try EDID(Self.makeEDID()).identity
        #expect(identity.hasPrefix("edid-"))
        #expect(identity.count == "edid-".count + 16)
        #expect(try EDID(Self.makeEDID()).identity == identity)
        #expect(try EDID(Self.makeEDID(product: 0x1234)).identity != identity)
    }

    @Test func ignoresExtensionBlocks() throws {
        let base = Self.makeEDID(serial: 7)
        let withExtension = base + [UInt8](repeating: 0xAB, count: 128)
        #expect(try EDID(withExtension) == EDID(base))
    }

    @Test func rejectsInvalidData() {
        var corrupt = Self.makeEDID()
        corrupt[20] &+= 1
        #expect(throws: EDID.ParseError.badChecksum) { try EDID(corrupt) }
        #expect(throws: EDID.ParseError.tooShort(10)) { try EDID([UInt8](repeating: 0, count: 10)) }
        var badHeader = Self.makeEDID()
        badHeader[0] = 0x01
        badHeader[127] &-= 1
        #expect(throws: EDID.ParseError.badHeader) { try EDID(badHeader) }
    }
}
