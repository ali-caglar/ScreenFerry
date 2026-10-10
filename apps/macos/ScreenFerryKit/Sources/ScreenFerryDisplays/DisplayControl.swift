import CoreGraphics
import Foundation

/// Private SkyLight call that enables or disables a display, the approach BetterDisplay uses.
/// Resolved at runtime so a macOS that drops it disables detach instead of crashing at launch.
private struct DisplayEnableAPI: Sendable {
    typealias ConfigureEnabled = @convention(c) (CGDisplayConfigRef?, CGDirectDisplayID, Bool) -> CGError
    typealias GetDisplayList = @convention(c) (UInt32, UnsafeMutablePointer<CGDirectDisplayID>?, UnsafeMutablePointer<UInt32>?) -> CGError

    let configureEnabled: ConfigureEnabled
    /// Unlike the public lists, this one includes detached displays.
    let allDisplays: GetDisplayList?

    static let shared: DisplayEnableAPI? = {
        let skyLight = dlopen("/System/Library/PrivateFrameworks/SkyLight.framework/SkyLight", RTLD_NOW)
        let coreGraphics = dlopen("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", RTLD_NOW)
        guard let configure = dlsym(skyLight, "SLSConfigureDisplayEnabled") ?? dlsym(coreGraphics, "CGSConfigureDisplayEnabled")
        else { return nil }
        let list = dlsym(skyLight, "SLSGetDisplayList")
        return DisplayEnableAPI(
            configureEnabled: unsafeBitCast(configure, to: ConfigureEnabled.self),
            allDisplays: list.map { unsafeBitCast($0, to: GetDisplayList.self) }
        )
    }()
}

public struct DisplayInfo: Equatable, Sendable {
    public let id: CGDirectDisplayID
    /// Packed PNP manufacturer ID, as in EDID bytes 8–9.
    public let vendor: UInt32
    public let model: UInt32
    public let serial: UInt32
    public let isBuiltin: Bool
    public let isMain: Bool
    public let isActive: Bool
    public let bounds: CGRect
}

public enum DisplayControlError: Error, CustomStringConvertible {
    case unavailable
    case unknownDisplay(CGDirectDisplayID)
    case wouldLeaveNoDisplay
    case failed(String, CGError)

    public var description: String {
        switch self {
        case .unavailable:
            "Display enable/disable is unavailable on this macOS (private SkyLight API missing)."
        case let .unknownDisplay(id):
            "No display with id \(id)."
        case .wouldLeaveNoDisplay:
            "Refusing to detach the last active display."
        case let .failed(operation, error):
            "\(operation) failed (CGError \(error.rawValue))."
        }
    }
}

public enum DisplayControl {
    public static var isSupported: Bool { DisplayEnableAPI.shared != nil }

    /// Connected displays macOS reports. Detached displays are not included.
    public static func displays() -> [DisplayInfo] {
        info(for: list(CGGetOnlineDisplayList))
    }

    /// Every display id the window server knows, including detached ones.
    /// One monitor can appear under several ids after its link drops and comes back.
    public static func allDisplays() -> [DisplayInfo] {
        guard let all = DisplayEnableAPI.shared?.allDisplays else { return displays() }
        return info(for: list(all))
    }

    private static func info(for ids: [CGDirectDisplayID]) -> [DisplayInfo] {
        let active = Set(list(CGGetActiveDisplayList))
        return ids.map { id in
            DisplayInfo(
                id: id,
                vendor: CGDisplayVendorNumber(id),
                model: CGDisplayModelNumber(id),
                serial: CGDisplaySerialNumber(id),
                isBuiltin: CGDisplayIsBuiltin(id) != 0,
                isMain: CGDisplayIsMain(id) != 0,
                isActive: active.contains(id),
                bounds: CGDisplayBounds(id)
            )
        }
    }

    /// Detaches (`false`) or re-attaches (`true`) a display for this login session.
    /// macOS moves windows off a detached display to the remaining ones. Retries briefly,
    /// since enabling fails while the display's link is retraining.
    public static func setEnabled(
        _ id: CGDirectDisplayID,
        _ enabled: Bool,
        attempts: Int = 5,
        retryDelay: Duration = .seconds(1)
    ) throws(DisplayControlError) {
        var lastError = DisplayControlError.unavailable
        for attempt in 0..<max(attempts, 1) {
            if attempt > 0 { Thread.sleep(forTimeInterval: Double(retryDelay.components.seconds)) }
            do {
                try configure(id, enabled)
                return
            } catch .failed(let operation, let error) {
                lastError = .failed(operation, error)
            }
        }
        throw lastError
    }

    private static func configure(_ id: CGDirectDisplayID, _ enabled: Bool) throws(DisplayControlError) {
        guard let api = DisplayEnableAPI.shared else { throw .unavailable }
        if !enabled {
            // A detached display disappears from the online list, so only disabling can be checked.
            let all = displays()
            guard all.contains(where: { $0.id == id }) else { throw .unknownDisplay(id) }
            if all.filter({ $0.isActive && $0.id != id }).isEmpty { throw .wouldLeaveNoDisplay }
        }

        var config: CGDisplayConfigRef?
        let begin = CGBeginDisplayConfiguration(&config)
        guard begin == .success else { throw .failed("CGBeginDisplayConfiguration", begin) }
        let change = api.configureEnabled(config, id, enabled)
        guard change == .success else {
            CGCancelDisplayConfiguration(config)
            throw .failed(enabled ? "Enabling display" : "Disabling display", change)
        }
        let complete = CGCompleteDisplayConfiguration(config, .forSession)
        guard complete == .success else { throw .failed("CGCompleteDisplayConfiguration", complete) }
    }

    private static func list(
        _ query: (UInt32, UnsafeMutablePointer<CGDirectDisplayID>?, UnsafeMutablePointer<UInt32>?) -> CGError
    ) -> [CGDirectDisplayID] {
        var count: UInt32 = 0
        guard query(0, nil, &count) == .success, count > 0 else { return [] }
        var ids = [CGDirectDisplayID](repeating: 0, count: Int(count))
        guard query(count, &ids, &count) == .success else { return [] }
        return Array(ids.prefix(Int(count)))
    }
}
