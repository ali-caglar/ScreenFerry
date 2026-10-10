/// Parses an MCCS capabilities string such as `(prot(monitor)vcp(10 60(0F 11 12))mccs_ver(2.1))`.
public struct MCCSCapabilities: Equatable, Sendable {
    public let raw: String
    /// VCP codes the monitor reports, with their allowed values when listed.
    public let vcp: [UInt8: [UInt8]]

    public init(_ raw: String) {
        self.raw = raw
        vcp = Self.section("vcp", in: raw).map(Self.parseVCP) ?? [:]
    }

    /// Standard MCCS names for input source (VCP 0x60) values. Monitors often deviate.
    public static func inputSourceName(_ value: UInt16) -> String? {
        switch value & 0xFF {
        case 0x01: "VGA 1"
        case 0x02: "VGA 2"
        case 0x03: "DVI 1"
        case 0x04: "DVI 2"
        case 0x0F: "DisplayPort 1"
        case 0x10: "DisplayPort 2"
        case 0x11: "HDMI 1"
        case 0x12: "HDMI 2"
        case 0x1B: "USB-C"
        default: nil
        }
    }

    private static func section(_ name: String, in raw: String) -> Substring? {
        guard let start = raw.range(of: name + "(", options: .caseInsensitive) else { return nil }
        var depth = 1
        var index = start.upperBound
        while index < raw.endIndex {
            switch raw[index] {
            case "(": depth += 1
            case ")":
                depth -= 1
                if depth == 0 { return raw[start.upperBound..<index] }
            default: break
            }
            index = raw.index(after: index)
        }
        return raw[start.upperBound...]
    }

    private static func parseVCP(_ body: Substring) -> [UInt8: [UInt8]] {
        var result: [UInt8: [UInt8]] = [:]
        var lastCode: UInt8?
        var token = ""
        var values: [UInt8]?

        func flush() {
            guard let byte = UInt8(token, radix: 16) else { token = ""; return }
            if values != nil {
                values?.append(byte)
            } else {
                result[byte] = []
                lastCode = byte
            }
            token = ""
        }

        for character in body {
            switch character {
            case "(":
                flush()
                values = []
            case ")":
                flush()
                if let code = lastCode, let list = values { result[code] = list }
                values = nil
            case " ":
                flush()
            default:
                token.append(character)
            }
        }
        flush()
        if let code = lastCode, let list = values { result[code] = list }
        return result
    }
}
