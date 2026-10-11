import Combine
import Foundation
import ScreenFerryDisplays
import ScreenFerryKit
import ScreenFerryNet
import ServiceManagement

/// Runs this Mac's agent for the menu-bar app.
@MainActor
public final class AgentController: ObservableObject, HandoffExecutor {
    public struct SceneEntry: Identifiable, Equatable {
        public var scene: Scene
        public var isBuiltIn: Bool
        /// Every computer the scene names is connected and sees its monitors.
        public var isAvailable: Bool
        /// ⌃⌥⌘ and this digit, for the first nine scenes.
        public var hotkey: Int?

        public var id: String { scene.id }
    }

    /// A monitor some computer sees, and which computers see it.
    public struct KnownMonitor: Identifiable, Equatable {
        public var identity: String
        public var name: String
        /// Key IDs (hex).
        public var seenBy: [String]

        public var id: String { identity }
    }

    @Published public private(set) var agent: Agent?
    @Published public private(set) var startError: String?
    @Published public private(set) var scenes: [SceneEntry] = []
    @Published public private(set) var knownMonitors: [KnownMonitor] = []
    @Published public private(set) var ambiguousMonitors: [String] = []
    @Published public private(set) var sceneError: String?
    @Published public private(set) var localMonitors: [ProtocolMessage.MonitorStatus] = []
    @Published public private(set) var opensAtLogin = SMAppService.mainApp.status == .enabled
    /// Why the login item isn't on, if macOS needs the user's approval or refused.
    @Published public private(set) var loginItemNote: String?

    private let displays = DisplayHandoff()
    private let sceneStore = JSONFileStore<[Scene]>.scenes()
    private let inputCodeStore = JSONFileStore<[String: Int]>.inputCodes()
    private var customScenes: [Scene] = []
    private var inputCodes: [String: Int] = [:]
    /// Attached monitors whose input code this Mac already tried to read.
    private var probed: Set<String> = []
    private var monitorTimer: Timer?
    private var hotKeys: HotKeys?
    private var subscriptions: [AnyCancellable] = []
    private let hud = HUD()
    private var shownHandoff: Agent.HandoffStatus?

    public init() {
        do {
            let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
            let agent = try Agent(identity: .keychain(), name: Host.current().localizedName ?? "Mac",
                                  appVersion: version, store: .standard())
            agent.executor = self
            customScenes = (try? sceneStore.load(default: [])) ?? []
            inputCodes = (try? inputCodeStore.load(default: [:])) ?? [:]
            _ = try? displays.reconcile()
            try agent.start()
            self.agent = agent
            hotKeys = HotKeys { [weak self] index in self?.activateHotkey(index) }
            subscriptions.append(agent.$peers.sink { [weak self] _ in
                DispatchQueue.main.async { MainActor.assumeIsolated { self?.updateScenes() } }
            })
            subscriptions.append(agent.$handoff.sink { [weak self] status in
                DispatchQueue.main.async { MainActor.assumeIsolated { self?.showProgress(status) } }
            })
            refreshMonitors()
            monitorTimer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.refreshMonitors() }
            }
        } catch {
            startError = "\(error)"
        }
    }

    public func setOpensAtLogin(_ on: Bool) {
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            loginItemNote = nil
        } catch {
            loginItemNote = error.localizedDescription
        }
        let status = SMAppService.mainApp.status
        opensAtLogin = status == .enabled
        if status == .requiresApproval {
            loginItemNote = "Allow ScreenFerry in System Settings → General → Login Items."
        }
    }

    // MARK: Scenes

    public func activate(_ entry: SceneEntry) {
        guard let agent, entry.isAvailable else { return }
        sceneError = nil
        agent.activate(owners: entry.scene.owners)
    }

    public func addScene(name: String, owners: [String: String]) {
        customScenes.append(Scene(name: name, owners: owners))
        saveScenes()
    }

    public func deleteScene(_ id: String) {
        customScenes.removeAll { $0.id == id }
        saveScenes()
    }

    /// The computers a scene can name: this Mac and the paired peers, key ID → name.
    public var computers: [(id: String, name: String)] {
        guard let agent else { return [] }
        return [(agent.keyID, agent.name)] + agent.peers.filter(\.isPaired).map { ($0.id, $0.name) }
    }

    private func saveScenes() {
        do {
            try sceneStore.save(customScenes)
        } catch {
            sceneError = "Couldn't save scenes: \(error)"
        }
        updateScenes()
    }

    private func activateHotkey(_ index: Int) {
        guard let entry = scenes.first(where: { $0.hotkey == index }) else { return }
        if entry.isAvailable {
            activate(entry)
        } else {
            sceneError = "\(entry.scene.name) needs computers that aren't connected."
            hud.show(entry.scene.name, detail: "Needs computers that aren't connected.", style: .failed)
        }
    }

    private func updateScenes() {
        guard let agent else { return }
        var seen = [agent.keyID: localMonitors]
        for peer in agent.peers where peer.isConnected { seen[peer.id] = peer.monitors }

        // One entry per physical monitor, named by the first computer's identity for it (ADR 0007).
        var groups: [(reference: ProtocolMessage.MonitorStatus, seenBy: [String])] = []
        for computer in computers {
            for monitor in seen[computer.id] ?? [] {
                if let index = groups.firstIndex(where: { !$0.seenBy.contains(computer.id) && MonitorMatch.find($0.reference, in: seen[computer.id] ?? []) == monitor }) {
                    groups[index].seenBy.append(computer.id)
                } else {
                    groups.append((monitor, [computer.id]))
                }
            }
        }
        let known = groups.map { KnownMonitor(identity: $0.reference.identity, name: $0.reference.name ?? $0.reference.identity, seenBy: $0.seenBy) }
            .sorted { $0.identity < $1.identity }
        func sees(_ computer: String, _ identity: String) -> Bool {
            guard let reference = seen.values.lazy.flatMap({ $0 }).first(where: { $0.identity == identity }) else { return false }
            return MonitorMatch.find(reference, in: seen[computer] ?? []) != nil
        }

        var entries: [SceneEntry] = []
        for computer in computers {
            let shared = known.filter { $0.seenBy.contains(computer.id) && $0.seenBy.count > 1 }
            let scene = Scene(id: "all:\(computer.id)", name: "All Monitors → \(computer.name)",
                              owners: Dictionary(shared.map { ($0.identity, computer.id) }) { a, _ in a })
            entries.append(SceneEntry(scene: scene, isBuiltIn: true, isAvailable: !shared.isEmpty))
        }
        for scene in customScenes.sorted(by: { $0.name.localizedStandardCompare($1.name) == .orderedAscending }) {
            let available = !scene.owners.isEmpty && scene.owners.allSatisfy { identity, owner in sees(owner, identity) }
            entries.append(SceneEntry(scene: scene, isBuiltIn: false, isAvailable: available))
        }
        for index in entries.indices where index < 9 { entries[index].hotkey = index + 1 }

        if known != knownMonitors { knownMonitors = known }
        if entries != scenes {
            scenes = entries
            hotKeys?.register(count: entries.count)
        }
    }

    /// The EDID name of a monitor reported under this identity by any computer.
    public func monitorName(_ identity: String) -> String {
        let reported = localMonitors + (agent?.peers.flatMap(\.monitors) ?? [])
        return reported.first { $0.identity == identity }?.name ?? identity
    }

    /// Shows a handoff's start, progress and outcome in the HUD, on every computer involved.
    private func showProgress(_ status: Agent.HandoffStatus?) {
        guard let status, status != shownHandoff else { return }
        let previous = shownHandoff?.seq == status.seq ? shownHandoff : nil
        shownHandoff = status
        guard previous?.isFinished != true else { return }

        let computerNames = Dictionary(computers.map { ($0.id, $0.name) }) { a, _ in a }
        let moves = status.owners.sorted { $0.key < $1.key }.map { identity, owner in
            "\(monitorName(identity)) → \(computerNames[owner] ?? "?")"
        }
        let title = moves.joined(separator: ", ")
        if !status.failures.isEmpty {
            let messages = status.failures.sorted { $0.key < $1.key }.map(\.value)
            hud.show("Couldn't move monitors", detail: messages.joined(separator: "\n"), style: .failed)
        } else if status.pending.isEmpty {
            hud.show(previous == nil ? "Already in place" : title, detail: previous == nil ? title : nil, style: .done)
        } else if status.isExpired {
            hud.show("Didn't finish", detail: title, style: .failed)
        } else if previous == nil {
            hud.show(title, detail: "Moving… a monitor without DDC/CI may need up to 25 s.", style: .progress)
        }
    }

    // MARK: Monitors

    private func refreshMonitors() {
        let scan = LocalMonitors.scan()
        probed.formIntersection(scan.monitors.filter(\.attached).map(\.identity))
        localMonitors = scan.monitors.map { monitor in
            var monitor = monitor
            monitor.inputCode = inputCodes[monitor.identity]
            return monitor
        }
        if scan.ambiguous != ambiguousMonitors { ambiguousMonitors = scan.ambiguous }
        agent?.setLocalMonitors(localMonitors)
        updateScenes()
        learnInputCodes()
    }

    /// Reads VCP `0x60` once per attach of each monitor: that value selects this Mac (BRIEF §3.2).
    private func learnInputCodes() {
        let elsewhere = Set(agent?.peers.filter(\.isConnected).flatMap { $0.monitors.filter(\.attached).map(\.identity) } ?? [])
        // While another computer has it attached too, the monitor may be showing that one.
        for monitor in localMonitors where monitor.attached && !probed.contains(monitor.identity) && !elsewhere.contains(monitor.identity) {
            probed.insert(monitor.identity)
            let identity = monitor.identity
            Task.detached {
                guard let code = try? InputSource.read(identity) else { return }
                await self.learned(code, for: identity)
            }
        }
    }

    private func learned(_ code: Int, for identity: String) {
        guard inputCodes[identity] != code else { return }
        inputCodes[identity] = code
        try? inputCodeStore.save(inputCodes)
        refreshMonitors()
    }

    // MARK: HandoffExecutor

    public func perform(_ step: HandoffStep, on identity: String) async throws {
        let displays = displays
        let ownCode = inputCodes[identity]
        defer { refreshMonitors() }
        try await Task.detached {
            switch step {
            case .attach:
                try displays.take(identity)
                if let ownCode { try? InputSource.set(identity, to: ownCode) }
            case let .switchAndRelease(code):
                try InputSource.set(identity, to: code)
                try displays.release(identity)
            case .releaseAfterSettle:
                try displays.release(identity)
            case .none, .waitForRelease, .waitForAttach, .missing:
                break
            }
        }.value
    }
}
