import Foundation
import IOKit
import ScreenFerryKit

public enum DDCError: Error, CustomStringConvertible {
    case unavailable
    case ioError(String, IOReturn)
    case noReply(underlying: DDCPacket.DecodeError)
    case unsupportedVCPCode(UInt8)

    public var description: String {
        switch self {
        case .unavailable:
            "DDC is unavailable: this Mac has no IOAVService (Intel Macs are not supported yet)."
        case let .ioError(operation, code):
            "\(operation) failed (IOReturn 0x\(String(UInt32(bitPattern: code), radix: 16))): the monitor did not answer. "
                + "Check that DDC/CI is on in its menu and that no dock, hub or adapter is in between."
        case let .noReply(underlying):
            "No valid reply from the monitor (\(underlying)). Check that DDC/CI is enabled in the monitor's menu."
        case let .unsupportedVCPCode(code):
            "The monitor reports VCP 0x\(String(format: "%02X", code)) as unsupported."
        }
    }
}

/// An external display reachable over DDC/CI through a `DCPAVServiceProxy`.
public final class ExternalDisplay: @unchecked Sendable {
    public let registryID: UInt64
    public let rawEDID: Data
    public let edid: EDID?

    private let service: CFTypeRef
    private let api: IOAVServiceAPI
    private let lock = NSLock()

    // Delays and retries follow MonitorControl's Apple Silicon defaults.
    private let writeCycles = 2
    private let writeDelay: UInt32 = 10_000
    private let replyDelay: UInt32 = 50_000
    private let attempts = 4

    public static var isSupported: Bool { IOAVServiceAPI.shared != nil }

    public static func all() throws(DDCError) -> [ExternalDisplay] {
        guard let api = IOAVServiceAPI.shared else { throw .unavailable }
        var iterator = io_iterator_t()
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("DCPAVServiceProxy"), &iterator) == KERN_SUCCESS
        else { return [] }
        defer { IOObjectRelease(iterator) }

        var displays: [ExternalDisplay] = []
        while case let entry = IOIteratorNext(iterator), entry != 0 {
            defer { IOObjectRelease(entry) }
            let location = IORegistryEntryCreateCFProperty(entry, "Location" as CFString, kCFAllocatorDefault, 0)?
                .takeRetainedValue() as? String
            guard location == "External",
                  let service = api.createWithService(kCFAllocatorDefault, entry)?.takeRetainedValue()
            else { continue }
            var registryID: UInt64 = 0
            IORegistryEntryGetRegistryEntryID(entry, &registryID)
            var edidRef: Unmanaged<CFData>?
            let rawEDID = api.copyEDID(service, &edidRef) == kIOReturnSuccess
                ? (edidRef?.takeRetainedValue() as Data?) ?? Data()
                : Data()
            displays.append(ExternalDisplay(registryID: registryID, rawEDID: rawEDID, service: service, api: api))
        }
        return displays
    }

    private init(registryID: UInt64, rawEDID: Data, service: CFTypeRef, api: IOAVServiceAPI) {
        self.registryID = registryID
        self.rawEDID = rawEDID
        self.edid = try? EDID(rawEDID)
        self.service = service
        self.api = api
    }

    public func getVCP(_ code: UInt8) throws(DDCError) -> DDCPacket.VCPValue {
        try exchange(request: DDCPacket.getVCP(code), replyLength: DDCPacket.getVCPReplyLength) { reply throws(DDCPacket.DecodeError) in
            try DDCPacket.decodeGetVCPReply(reply, code: code)
        }
    }

    public func setVCP(_ code: UInt8, value: UInt16) throws(DDCError) {
        lock.lock()
        defer { lock.unlock() }
        try write(DDCPacket.setVCP(code, value: value))
    }

    public func capabilities() throws(DDCError) -> MCCSCapabilities {
        var bytes: [UInt8] = []
        while bytes.count < 8_192 {
            let offset = UInt16(bytes.count)
            let fragment = try exchange(
                request: DDCPacket.capabilities(offset: offset),
                replyLength: DDCPacket.capabilitiesReplyMaxLength
            ) { reply throws(DDCPacket.DecodeError) in
                try DDCPacket.decodeCapabilitiesReply(reply, offset: offset)
            }
            if fragment.isEmpty { break }
            bytes += fragment
        }
        return MCCSCapabilities(String(decoding: bytes.prefix { $0 != 0 }, as: UTF8.self))
    }

    private func exchange<T>(
        request: [UInt8],
        replyLength: Int,
        decode: ([UInt8]) throws(DDCPacket.DecodeError) -> T
    ) throws(DDCError) -> T {
        lock.lock()
        defer { lock.unlock() }
        var lastError = DDCError.ioError("IOAVServiceReadI2C", kIOReturnError)
        for _ in 0..<attempts {
            try write(request)
            usleep(replyDelay)
            var reply = [UInt8](repeating: 0, count: replyLength)
            let status = reply.withUnsafeMutableBytes {
                api.readI2C(service, UInt32(DDCPacket.chipAddress), UInt32(DDCPacket.hostAddress), $0.baseAddress!, UInt32(replyLength))
            }
            guard status == kIOReturnSuccess else {
                lastError = .ioError("IOAVServiceReadI2C", status)
                continue
            }
            do {
                return try decode(reply)
            } catch DDCPacket.DecodeError.unsupportedVCPCode(let code) {
                throw .unsupportedVCPCode(code)
            } catch {
                lastError = .noReply(underlying: error)
            }
        }
        throw lastError
    }

    private func write(_ payload: [UInt8]) throws(DDCError) {
        var payload = payload
        for cycle in 0..<writeCycles {
            if cycle > 0 { usleep(writeDelay) }
            let status = payload.withUnsafeMutableBytes {
                api.writeI2C(service, UInt32(DDCPacket.chipAddress), UInt32(DDCPacket.hostAddress), $0.baseAddress!, UInt32($0.count))
            }
            guard status == kIOReturnSuccess else { throw .ioError("IOAVServiceWriteI2C", status) }
        }
    }
}
