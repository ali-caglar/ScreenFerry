import Foundation
import IOKit

/// Private IOKit functions for DDC/CI over I2C on Apple Silicon (same approach as m1ddc).
/// Resolved at runtime so a future macOS that drops them disables DDC instead of crashing at launch.
struct IOAVServiceAPI: Sendable {
    typealias CreateWithService = @convention(c) (CFAllocator?, io_service_t) -> Unmanaged<CFTypeRef>?
    typealias CopyEDID = @convention(c) (CFTypeRef, UnsafeMutablePointer<Unmanaged<CFData>?>) -> IOReturn
    typealias ReadI2C = @convention(c) (CFTypeRef, UInt32, UInt32, UnsafeMutableRawPointer, UInt32) -> IOReturn
    typealias WriteI2C = @convention(c) (CFTypeRef, UInt32, UInt32, UnsafeMutableRawPointer, UInt32) -> IOReturn

    let createWithService: CreateWithService
    let copyEDID: CopyEDID
    let readI2C: ReadI2C
    let writeI2C: WriteI2C

    static let shared: IOAVServiceAPI? = {
        guard let handle = dlopen("/System/Library/Frameworks/IOKit.framework/IOKit", RTLD_NOW),
              let create = dlsym(handle, "IOAVServiceCreateWithService"),
              let copyEDID = dlsym(handle, "IOAVServiceCopyEDID"),
              let read = dlsym(handle, "IOAVServiceReadI2C"),
              let write = dlsym(handle, "IOAVServiceWriteI2C")
        else { return nil }
        return IOAVServiceAPI(
            createWithService: unsafeBitCast(create, to: CreateWithService.self),
            copyEDID: unsafeBitCast(copyEDID, to: CopyEDID.self),
            readI2C: unsafeBitCast(read, to: ReadI2C.self),
            writeI2C: unsafeBitCast(write, to: WriteI2C.self)
        )
    }()
}
