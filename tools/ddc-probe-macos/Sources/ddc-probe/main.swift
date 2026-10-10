import Foundation
import ScreenFerryDDC
import ScreenFerryKit

let usageText = """
    Usage: ddc-probe <command>

      list                          External monitors, their identity, input and brightness
      edid <display>                Hex dump of the monitor's EDID
      caps <display>                MCCS capabilities string and supported VCP codes
      get  <display> <vcp>          Read a VCP code, e.g. `get 1 0x60`
      set  <display> <vcp> <value>  Write a VCP code, e.g. `set 1 0x10 50`

    <display> is the number shown by `list`. Numbers are decimal or 0x-prefixed hex.
    Useful codes: 0x10 brightness (harmless test), 0x60 input source.
    """

enum ProbeError: Error, CustomStringConvertible {
    case usage(String)

    var description: String {
        switch self {
        case let .usage(message): "\(message)\n\n\(usageText)"
        }
    }
}

func parseNumber(_ text: String) throws -> Int {
    let value = text.lowercased().hasPrefix("0x") ? Int(text.dropFirst(2), radix: 16) : Int(text)
    guard let value, value >= 0 else { throw ProbeError.usage("Not a number: \(text)") }
    return value
}

func hex(_ value: some BinaryInteger, width: Int = 2) -> String {
    "0x" + String(format: "%0\(width)X", Int(value))
}

func display(_ argument: String?, in displays: [ExternalDisplay]) throws -> ExternalDisplay {
    guard let argument else { throw ProbeError.usage("Missing <display>.") }
    let index = try parseNumber(argument)
    guard displays.indices.contains(index - 1) else {
        throw ProbeError.usage("No display \(index); `list` shows \(displays.count).")
    }
    return displays[index - 1]
}

func vcpCode(_ argument: String?) throws -> UInt8 {
    guard let argument else { throw ProbeError.usage("Missing <vcp>.") }
    let code = try parseNumber(argument)
    guard code <= 0xFF else { throw ProbeError.usage("VCP codes are 0x00–0xFF.") }
    return UInt8(code)
}

func describe(_ code: UInt8, _ value: DDCPacket.VCPValue) -> String {
    if code == 0x60 {
        let name = MCCSCapabilities.inputSourceName(value.current).map { " (\($0))" } ?? ""
        return "\(hex(value.current & 0xFF))\(name), raw \(hex(value.current, width: 4))"
    }
    return "\(value.current) of \(value.maximum)"
}

func list(_ displays: [ExternalDisplay]) {
    if displays.isEmpty {
        print("No external displays with a DDC service found.")
        return
    }
    for (offset, display) in displays.enumerated() {
        let edid = display.edid
        let title = [edid?.name, edid.map { "\($0.manufacturerID) \(hex($0.productCode, width: 4))" }]
            .compactMap { $0 }.joined(separator: " — ")
        print("\(offset + 1). \(title.isEmpty ? "Unknown monitor" : title)")
        print("   identity:   \(edid?.identity ?? "unreadable EDID (\(display.rawEDID.count) bytes)")")
        if let edid {
            let serial = [edid.serialString, edid.serialNumber == 0 ? nil : String(edid.serialNumber)]
                .compactMap { $0 }.joined(separator: " / ")
            print("   serial:     \(serial.isEmpty ? "none" : serial), made \(edid.manufactureYear)")
        }
        for (label, code) in [("input", UInt8(0x60)), ("brightness", UInt8(0x10))] {
            let result: String
            do {
                result = describe(code, try display.getVCP(code))
            } catch {
                result = "error: \(error)"
            }
            print("   \(label.padding(toLength: 11, withPad: " ", startingAt: 0)) \(result)")
        }
    }
}

func run(_ arguments: [String]) throws {
    guard let command = arguments.first, command != "help", command != "-h", command != "--help" else {
        print(usageText)
        return
    }
    let displays = try ExternalDisplay.all()
    let rest = Array(arguments.dropFirst())

    switch command {
    case "list":
        list(displays)

    case "edid":
        let target = try display(rest.first, in: displays)
        for start in stride(from: 0, to: target.rawEDID.count, by: 16) {
            let row = target.rawEDID[start..<min(start + 16, target.rawEDID.count)]
            print(String(format: "%04x  ", start) + row.map { String(format: "%02x", $0) }.joined(separator: " "))
        }

    case "caps":
        let target = try display(rest.first, in: displays)
        let caps = try target.capabilities()
        print(caps.raw)
        print()
        for code in caps.vcp.keys.sorted() {
            let values = caps.vcp[code] ?? []
            print("\(hex(code))" + (values.isEmpty ? "" : ": " + values.map { hex($0) }.joined(separator: " ")))
        }

    case "get":
        let target = try display(rest.first, in: displays)
        let code = try vcpCode(rest.dropFirst().first)
        print(describe(code, try target.getVCP(code)))

    case "set":
        let target = try display(rest.first, in: displays)
        let code = try vcpCode(rest.dropFirst().first)
        guard let valueText = rest.dropFirst(2).first else { throw ProbeError.usage("Missing <value>.") }
        let value = try parseNumber(valueText)
        guard value <= 0xFFFF else { throw ProbeError.usage("Values are 0–65535.") }
        if code == 0x60 {
            print("Switching input to \(hex(value)). If that input isn't this Mac, the monitor leaves this Mac now.")
        }
        try target.setVCP(code, value: UInt16(value))
        print("Sent \(hex(code)) = \(value) (\(hex(value, width: 4))).")

    default:
        throw ProbeError.usage("Unknown command: \(command)")
    }
}

do {
    try run(Array(CommandLine.arguments.dropFirst()))
} catch let error as ProbeError {
    FileHandle.standardError.write(Data("\(error)\n".utf8))
    exit(2)
} catch {
    FileHandle.standardError.write(Data("error: \(error)\n".utf8))
    exit(1)
}
