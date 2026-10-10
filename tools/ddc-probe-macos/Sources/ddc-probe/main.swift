import CoreGraphics
import Foundation
import ScreenFerryDDC
import ScreenFerryDisplays
import ScreenFerryKit

let usageText = """
    Usage: ddc-probe <command>

      list                          External monitors, their identity, input and brightness
      edid <display>                Hex dump of the monitor's EDID
      caps <display>                MCCS capabilities string and supported VCP codes
      get  <display> <vcp>          Read a VCP code, e.g. `get 1 0x60`
      set  <display> <vcp> <value>  Write a VCP code, e.g. `set 1 0x10 50`

      displays                      Displays known to macOS, with their CoreGraphics id
      detach <id> [seconds]         Detach a display, re-attach after `seconds` (default 10)
      detach <id> --keep            Detach and leave it detached
      attach <id>                   Re-attach a detached display

    <display> is the number shown by `list`; <id> is the id shown by `displays`.
    Numbers are decimal or 0x-prefixed hex.
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

func edidIdentity(for display: DisplayInfo, among externals: [ExternalDisplay]) -> String? {
    externals.compactMap(\.edid).first { edid in
        let vendor = UInt32(edid.baseBlock[8]) << 8 | UInt32(edid.baseBlock[9])
        return vendor == display.vendor && UInt32(edid.productCode) == display.model
            && (display.serial == 0 || edid.serialNumber == display.serial)
    }?.identity
}

func listDisplays() {
    let externals = (try? ExternalDisplay.all()) ?? []
    for display in DisplayControl.displays() {
        var tags = [display.isActive ? "attached" : "inactive"]
        if display.isBuiltin { tags.append("built-in") }
        if display.isMain { tags.append("main") }
        let identity = display.isBuiltin ? "" : edidIdentity(for: display, among: externals) ?? "identity unknown"
        let size = display.isActive ? " \(Int(display.bounds.width))x\(Int(display.bounds.height))" : ""
        print("id \(display.id): \(tags.joined(separator: ", "))\(size)  \(identity)")
    }
}

func displayID(_ argument: String?, mustBeListed: Bool = true) throws -> CGDirectDisplayID {
    guard let argument else { throw ProbeError.usage("Missing <id>.") }
    let id = CGDirectDisplayID(try parseNumber(argument))
    guard CGDisplayIsBuiltin(id) == 0 else { throw ProbeError.usage("Refusing to detach or attach the built-in display.") }
    if mustBeListed, !DisplayControl.displays().contains(where: { $0.id == id }) {
        throw ProbeError.usage("No display with id \(id); see `displays`.")
    }
    return id
}

func detach(_ id: CGDirectDisplayID, reattachAfter seconds: Int?) throws {
    try DisplayControl.setEnabled(id, false)
    guard let seconds else {
        print("Detached display \(id). Re-attach with `attach \(id)` (logging out also restores it).")
        return
    }
    print("Detached display \(id). Re-attaching in \(seconds) s; Ctrl+C re-attaches now.")
    signal(SIGINT, SIG_IGN)
    let done = DispatchSemaphore(value: 0)
    let interrupt = DispatchSource.makeSignalSource(signal: SIGINT, queue: .global())
    interrupt.setEventHandler { done.signal() }
    interrupt.resume()
    _ = done.wait(timeout: .now() + .seconds(seconds))
    interrupt.cancel()
    try DisplayControl.setEnabled(id, true)
    print("Re-attached display \(id).")
}

func run(_ arguments: [String]) throws {
    guard let command = arguments.first, command != "help", command != "-h", command != "--help" else {
        print(usageText)
        return
    }
    let rest = Array(arguments.dropFirst())

    switch command {
    case "displays":
        listDisplays()
        return
    case "detach":
        let id = try displayID(rest.first)
        let option = rest.dropFirst().first
        try detach(id, reattachAfter: option == "--keep" ? nil : try option.map(parseNumber) ?? 10)
        return
    case "attach":
        try DisplayControl.setEnabled(try displayID(rest.first, mustBeListed: false), true)
        print("Attached.")
        return
    default:
        break
    }

    let displays = try ExternalDisplay.all()
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
