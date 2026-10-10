import Combine
import Foundation
import ScreenFerryDisplays
import ScreenFerryKit
import ScreenFerryNet

/// Runs this Mac's agent for the menu-bar app.
@MainActor
public final class AgentController: ObservableObject {
    @Published public private(set) var agent: Agent?
    @Published public private(set) var startError: String?
    private var monitorTimer: Timer?

    public init() {
        do {
            let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
            let agent = try Agent(identity: .keychain(), name: Host.current().localizedName ?? "Mac",
                                  appVersion: version, store: .standard())
            try agent.start()
            self.agent = agent
            refreshMonitors()
            monitorTimer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.refreshMonitors() }
            }
        } catch {
            startError = "\(error)"
        }
    }

    private func refreshMonitors() {
        agent?.setLocalMonitors(LocalMonitors.current())
    }
}
