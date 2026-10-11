import Foundation
import Sparkle

/// Sparkle with automatic, signed updates (ADR 0009), installed only while no handoff runs.
@MainActor
final class Updates: NSObject, SPUUpdaterDelegate {
    private var controller: SPUStandardUpdaterController?
    private let isBusy: () -> Bool

    /// Nil when the bundle has no feed, e.g. under `swift test`.
    init?(isBusy: @escaping () -> Bool) {
        guard Bundle.main.object(forInfoDictionaryKey: "SUFeedURL") != nil else { return nil }
        self.isBusy = isBusy
        super.init()
        controller = SPUStandardUpdaterController(startingUpdater: true, updaterDelegate: self, userDriverDelegate: nil)
    }

    func checkForUpdates() {
        controller?.checkForUpdates(nil)
    }

    nonisolated func updater(_ updater: SPUUpdater, willInstallUpdateOnQuit item: SUAppcastItem,
                             immediateInstallationBlock: @escaping () -> Void) -> Bool {
        let install = UncheckedBlock(run: immediateInstallationBlock)
        Task { @MainActor in
            while isBusy() { try? await Task.sleep(for: .seconds(30)) }
            install.run()
        }
        return true
    }
}

private struct UncheckedBlock: @unchecked Sendable {
    let run: () -> Void
}
