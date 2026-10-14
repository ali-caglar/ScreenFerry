import Combine
import Foundation
import Network
import ScreenFerryKit

/// Does this computer's steps of a handoff (ADR 0007).
@MainActor
public protocol HandoffExecutor: AnyObject {
    /// Attaches or releases the monitor; the agent learns the result from `setLocalMonitors`.
    func perform(_ step: HandoffStep, on identity: String) async throws
}

/// Finds, pairs with and stays connected to the other computers' agents (ADR 0006), and
/// moves monitors between them (ADR 0007).
@MainActor
public final class Agent: ObservableObject {
    public struct Peer: Identifiable, Equatable, Sendable {
        /// Key ID, hex.
        public var id: String
        public var name: String
        public var platform: ProtocolMessage.Platform?
        public var isPaired: Bool
        public var isConnected: Bool
        /// Advertising pairing mode right now.
        public var isPairable: Bool
        public var monitors: [ProtocolMessage.MonitorStatus]
    }

    public struct PairingPrompt: Equatable, Sendable {
        public var peerName: String
        public var code: String
    }

    public struct HandoffStatus: Equatable, Sendable {
        public var seq: Int
        /// Monitor identity → owner key ID (hex).
        public var owners: [String: String]
        /// Identities not yet attached on their target only.
        public var pending: Set<String>
        /// Identity → why it failed, prefixed with the computer's name.
        public var failures: [String: String]
        public var isExpired: Bool

        public var isFinished: Bool { pending.isEmpty || isExpired || pending.isSubset(of: failures.keys) }
    }

    public static let pairingWindow: TimeInterval = 120
    static let pingInterval: TimeInterval = 15
    static let silenceLimit: TimeInterval = 45

    @Published public private(set) var peers: [Peer] = []
    @Published public private(set) var isPairingMode = false
    @Published public private(set) var pairingPrompt: PairingPrompt?
    /// Outcome of the last pairing attempt, for the UI.
    @Published public private(set) var pairingResult: String?
    @Published public private(set) var listenerError: String?
    @Published public private(set) var handoff: HandoffStatus?
    public weak var executor: HandoffExecutor?
    /// How long a monitor stays attached on its new owner before the old owner releases it.
    public var settle = Handoff.settle

    public let identity: AgentIdentity
    public let name: String
    let appVersion: String
    let store: PairedPeerStore

    private var paired: [String: PairedPeer] = [:]
    private var localMonitors: [ProtocolMessage.MonitorStatus] = []
    private var listener: NWListener?
    private var browser: NWBrowser?
    private var discovered: [String: Discovered] = [:]
    private var peerMonitors: [String: [ProtocolMessage.MonitorStatus]] = [:]
    private var links: [Link] = []
    private var pairingDeadline: Date?
    private var timer: Timer?
    private var clock = HandoffClock()
    private var inFlight: Set<String> = []
    private var failures: [String: String] = [:]
    private var targetAttachedSince: [String: Date] = [:]

    private struct Discovered {
        var endpoint: NWEndpoint
        var name: String
        var isPairable: Bool
        var since: Date
    }

    @MainActor
    private final class Link {
        let connection: PeerConnection
        let wantsToPair: Bool
        var peerKeyID: Data?
        var hello: ProtocolMessage.Hello?
        var pairing: PairingSession?
        var isPaired = false
        var lastSent = Date()

        init(connection: PeerConnection, wantsToPair: Bool) {
            self.connection = connection
            self.wantsToPair = wantsToPair
        }

        var peerID: String? { peerKeyID?.hex }

        func send(_ message: ProtocolMessage) {
            lastSent = Date()
            connection.send(message)
        }
    }

    public init(identity: AgentIdentity, name: String, appVersion: String, store: PairedPeerStore) throws {
        self.identity = identity
        self.name = name
        self.appVersion = appVersion
        self.store = store
        paired = Dictionary(try store.load().map { ($0.keyID, $0) }) { $1 }
        publish()
    }

    public var listenerPort: UInt16? { listener?.port?.rawValue }

    public var keyID: String { identity.keyID.hex }

    /// `advertise: false` skips DNS-SD (tests connect by port).
    public func start(port: NWEndpoint.Port = .any, advertise: Bool = true) throws {
        let listener = try NWListener(using: SecureChannel.parameters(identity: identity, queue: .main) { [weak self] keyID in
            MainActor.assumeIsolated { self?.acceptsIncoming(keyID) ?? false }
        }, on: port)
        listener.newConnectionHandler = { [weak self] connection in
            MainActor.assumeIsolated { self?.add(PeerConnection(connection: connection, outgoing: false, queue: .main), wantsToPair: false) }
        }
        listener.stateUpdateHandler = { [weak self] state in
            MainActor.assumeIsolated {
                if case let .failed(error) = state { self?.listenerError = "\(error)" }
            }
        }
        if advertise { listener.service = service() }
        listener.start(queue: .main)
        self.listener = listener
        if advertise { startBrowsing() }

        timer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
    }

    public func stop() {
        timer?.invalidate()
        browser?.cancel()
        listener?.cancel()
        for link in links { link.connection.close(nil) }
    }

    // MARK: User actions

    public func setPairingMode(_ on: Bool) {
        isPairingMode = on
        pairingDeadline = on ? Date().addingTimeInterval(Self.pairingWindow) : nil
        if on { pairingResult = nil }
        if listener?.service != nil { listener?.service = service() }
    }

    /// Starts pairing with a discovered computer; both must be in pairing mode.
    public func pair(with peerID: String) {
        guard let peer = discovered[peerID] else { return }
        pair(with: peer.endpoint)
    }

    public func pair(with endpoint: NWEndpoint) {
        if !isPairingMode { setPairingMode(true) }
        connect(to: endpoint, wantsToPair: true)
    }

    public func confirmPairing(_ accepted: Bool) {
        guard pairingPrompt != nil, let link = links.first(where: { $0.pairing != nil }), var session = link.pairing else { return }
        let events = session.userDecided(accepted)
        link.pairing = session
        handle(events, on: link)
    }

    public func unpair(_ peerID: String) {
        paired[peerID] = nil
        try? store.save(Array(paired.values))
        for link in links where link.peerID == peerID { link.connection.close(nil) }
        publish()
    }

    public func setLocalMonitors(_ monitors: [ProtocolMessage.MonitorStatus]) {
        guard monitors != localMonitors else { return }
        localMonitors = monitors
        for link in links where link.isPaired { link.send(.monitors(monitors)) }
        advanceHandoff()
    }

    /// Moves monitors to the given owners (identity → key ID hex) and tells every connected peer.
    public func activate(owners: [String: String]) {
        let target = clock.start(owners: owners, origin: keyID)
        resetHandoff()
        for link in links where link.isPaired { link.send(handoffMessage(target)) }
        advanceHandoff()
    }

    /// Gives each attached monitor that a connected peer also sees to that peer, before this Mac
    /// shuts down. Returns whether there was one.
    @discardableResult
    public func handOffBeforeLeaving() -> Bool {
        // Prefer a computer that has a monitor attached: the user is probably at it.
        let peers = links.filter(\.isPaired).compactMap(\.peerID)
            .map { (id: $0, monitors: peerMonitors[$0] ?? []) }
            .sorted { a, b in
                let (aAttached, bAttached) = (a.monitors.contains(where: \.attached), b.monitors.contains(where: \.attached))
                return aAttached != bAttached ? aAttached : a.id < b.id
            }
        var owners: [String: String] = [:]
        for monitor in localMonitors where monitor.attached {
            if let peer = peers.first(where: { MonitorMatch.find(monitor, in: $0.monitors) != nil }) {
                owners[monitor.identity] = peer.id
            }
        }
        guard !owners.isEmpty else { return false }
        activate(owners: owners)
        return true
    }

    /// Every monitor of the current handoff is attached on the computer it goes to.
    public var handoffReachedTargets: Bool {
        guard let target = clock.target else { return false }
        return target.owners.allSatisfy { monitor, owner in states(for: monitor).states[owner]?.attached == true }
    }

    /// Connects to a paired peer, e.g. one not found by DNS-SD.
    public func connect(to endpoint: NWEndpoint) {
        connect(to: endpoint, wantsToPair: false)
    }

    // MARK: Connections

    private func connect(to endpoint: NWEndpoint, wantsToPair: Bool) {
        let parameters = SecureChannel.parameters(identity: identity, queue: .main) { [weak self] keyID in
            MainActor.assumeIsolated { wantsToPair || self?.paired[keyID.hex] != nil }
        }
        add(PeerConnection(connection: NWConnection(to: endpoint, using: parameters), outgoing: true, queue: .main), wantsToPair: wantsToPair)
    }

    private func acceptsIncoming(_ keyID: Data) -> Bool {
        paired[keyID.hex] != nil || isPairingMode
    }

    private func add(_ connection: PeerConnection, wantsToPair: Bool) {
        let link = Link(connection: connection, wantsToPair: wantsToPair)
        links.append(link)
        connection.start { [weak self, weak link] event in
            MainActor.assumeIsolated {
                guard let link else { return }
                self?.handle(event, on: link)
            }
        }
    }

    private func handle(_ event: PeerConnection.Event, on link: Link) {
        switch event {
        case let .ready(peerKeyID):
            link.peerKeyID = peerKeyID
            link.send(.hello(.init(keyID: identity.keyID, name: name, platform: .macos, appVersion: appVersion)))
        case let .message(message):
            receive(message, on: link)
        case .closed:
            links.removeAll { $0 === link }
            advanceHandoff()
            if link.pairing != nil {
                pairingPrompt = nil
                pairingResult = "Pairing with \(link.hello?.name ?? "?") was interrupted."
            }
            publish()
        }
    }

    private func receive(_ message: ProtocolMessage, on link: Link) {
        guard let hello = link.hello else {
            guard case let .hello(hello) = message, hello.keyID == link.peerKeyID else {
                return reject(link, .badMessage)
            }
            link.hello = hello
            return helloReceived(on: link)
        }
        switch message {
        case .ping, .unknown, .hello:
            break
        case .pairCommit, .pairNonce, .pairConfirm:
            guard var session = link.pairing else { return reject(link, .badMessage) }
            let events = session.receive(message)
            link.pairing = session
            handle(events, on: link)
        case let .monitors(monitors):
            guard link.isPaired else { return reject(link, .notPaired) }
            peerMonitors[hello.keyID.hex] = monitors
            publish()
            advanceHandoff()
        case let .handoff(seq, owners):
            guard link.isPaired else { return reject(link, .notPaired) }
            let known = owners.filter { $0.owner == identity.keyID || paired[$0.owner.hex] != nil }
            let target = Dictionary(known.map { ($0.identity, $0.owner.hex) }) { first, _ in first }
            if clock.receive(seq: seq, owners: target, from: hello.keyID.hex) {
                resetHandoff()
                advanceHandoff()
            }
        case let .handoffFailed(seq, identity, message):
            guard link.isPaired else { return reject(link, .notPaired) }
            if seq == clock.target?.seq {
                failures[identity] = "\(hello.name): \(message)"
                advanceHandoff()
            }
        case .error:
            link.connection.close(nil)
        }
    }

    private func helloReceived(on link: Link) {
        guard let peerKeyID = link.peerKeyID, let hello = link.hello else { return }
        if var peer = paired[peerKeyID.hex] {
            if peer.name != hello.name || peer.platform != hello.platform {
                peer.name = hello.name
                peer.platform = hello.platform
                paired[peer.keyID] = peer
                try? store.save(Array(paired.values))
            }
            return becamePaired(link)
        }
        let pairingElsewhere = links.contains { $0 !== link && $0.pairing != nil }
        if !pairingElsewhere, link.connection.outgoing ? link.wantsToPair : isPairingMode {
            var session = PairingSession(role: link.connection.outgoing ? .initiator : .responder,
                                         localKeyID: identity.keyID, peerKeyID: peerKeyID)
            let events = session.start()
            link.pairing = session
            return handle(events, on: link)
        }
        reject(link, .notPaired)
    }

    private func becamePaired(_ link: Link) {
        guard let peerID = link.peerID else { return }
        // Both may have dialled; keep the connection opened by the lower key ID.
        if let other = links.first(where: { $0 !== link && $0.isPaired && $0.peerID == peerID }) {
            let keepThis = link.connection.outgoing == (identity.keyID.hex < peerID)
            (keepThis ? other : link).connection.close(nil)
            if !keepThis { return }
        }
        link.isPaired = true
        link.send(.monitors(localMonitors))
        if let target = clock.target, handoff?.isFinished == false { link.send(handoffMessage(target)) }
        publish()
    }

    private func handle(_ events: [PairingSession.Event], on link: Link) {
        for event in events {
            switch event {
            case let .send(message):
                link.send(message)
            case let .showCode(code):
                pairingPrompt = PairingPrompt(peerName: link.hello?.name ?? "?", code: code)
            case .paired:
                guard let peerKeyID = link.peerKeyID, let hello = link.hello else { return }
                paired[peerKeyID.hex] = PairedPeer(keyID: peerKeyID.hex, name: hello.name, platform: hello.platform, pairedAt: Date())
                try? store.save(Array(paired.values))
                link.pairing = nil
                pairingPrompt = nil
                pairingResult = "Paired with \(hello.name)."
                setPairingMode(false)
                becamePaired(link)
            case let .failed(failure):
                link.pairing = nil
                pairingPrompt = nil
                pairingResult = "Pairing with \(link.hello?.name ?? "?") failed: \(failure)."
                link.connection.finish()
            }
        }
    }

    private func reject(_ link: Link, _ code: ProtocolErrorCode) {
        link.send(.error(code))
        link.connection.finish()
    }

    private func tick() {
        let now = Date()
        if let pairingDeadline, now > pairingDeadline { setPairingMode(false) }
        advanceHandoff()
        for link in links {
            if now.timeIntervalSince(link.connection.lastReceived) > Self.silenceLimit {
                link.connection.close("Peer went silent.")
            } else if link.hello != nil, now.timeIntervalSince(link.lastSent) >= Self.pingInterval {
                link.send(.ping)
            }
        }
        for (peerID, peer) in discovered where paired[peerID] != nil && !links.contains(where: { $0.peerID == peerID || $0.connection.connection.endpoint == peer.endpoint }) {
            // The lower key ID dials; the other waits a little in case it can't reach us.
            if identity.keyID.hex < peerID || now.timeIntervalSince(peer.since) > 10 {
                connect(to: peer.endpoint)
            }
        }
    }

    // MARK: Handoff

    private func handoffMessage(_ target: HandoffTarget) -> ProtocolMessage {
        .handoff(seq: target.seq, owners: target.owners.sorted { $0.key < $1.key }.compactMap { identity, owner in
            Data(hex: owner).map { ProtocolMessage.Owner(identity: identity, owner: $0) }
        })
    }

    private func resetHandoff() {
        failures = [:]
        targetAttachedSince = [:]
    }

    /// What each connected computer, including this one, reports for the monitor, and this
    /// computer's identity for it, which may differ (ADR 0007).
    private func states(for monitor: String) -> (states: [String: Handoff.State], localIdentity: String?) {
        var reports = [(keyID, localMonitors)]
        for link in links where link.isPaired {
            guard let peerID = link.peerID else { continue }
            reports.append((peerID, peerMonitors[peerID] ?? []))
        }
        guard let reference = reports.lazy.flatMap(\.1).first(where: { $0.identity == monitor }) else { return ([:], nil) }
        var states: [String: Handoff.State] = [:]
        var localIdentity: String?
        for (id, monitors) in reports {
            guard let match = MonitorMatch.find(reference, in: monitors) else { continue }
            states[id] = Handoff.State(attached: match.attached, inputCode: match.inputCode)
            if id == keyID { localIdentity = match.identity }
        }
        return (states, localIdentity)
    }

    private func advanceHandoff() {
        guard let target = clock.target else { return }
        let now = Date()
        let isExpired = now.timeIntervalSince(target.startedAt) > Handoff.expiry
        var pending = Set<String>()
        for (monitor, owner) in target.owners.sorted(by: { $0.key < $1.key }) {
            let (states, localIdentity) = states(for: monitor)
            if states[owner]?.attached == true {
                targetAttachedSince[monitor] = targetAttachedSince[monitor] ?? now
            } else {
                targetAttachedSince[monitor] = nil
            }
            guard !Handoff.isDone(target: owner, states: states) else { continue }
            pending.insert(monitor)
            guard !isExpired, failures[monitor] == nil, !inFlight.contains(monitor) else { continue }
            let step = Handoff.step(local: keyID, target: owner, states: states)
            switch step {
            case .none, .waitForAttach, .waitForRelease:
                break
            case .missing:
                fail(monitor, seq: target.seq, "This computer doesn't see the monitor.")
            case .releaseAfterSettle:
                let remaining = settle - now.timeIntervalSince(targetAttachedSince[monitor] ?? now)
                if remaining > 0 {
                    Task { [weak self] in
                        try? await Task.sleep(for: .seconds(remaining))
                        self?.advanceHandoff()
                    }
                } else {
                    perform(step, on: monitor, as: localIdentity ?? monitor, seq: target.seq)
                }
            case .attach, .switchAndRelease:
                perform(step, on: monitor, as: localIdentity ?? monitor, seq: target.seq)
            }
        }
        let status = HandoffStatus(seq: target.seq, owners: target.owners, pending: pending, failures: failures, isExpired: isExpired)
        if status != handoff { handoff = status }
    }

    /// `localIdentity`: this computer's identity for `monitor`.
    private func perform(_ step: HandoffStep, on monitor: String, as localIdentity: String, seq: Int) {
        guard let executor else { return fail(monitor, seq: seq, "This computer can't move monitors.") }
        inFlight.insert(monitor)
        Task { [weak self] in
            do {
                try await executor.perform(step, on: localIdentity)
            } catch {
                self?.fail(monitor, seq: seq, "\(error)")
            }
            self?.inFlight.remove(monitor)
            self?.advanceHandoff()
        }
    }

    private func fail(_ monitor: String, seq: Int, _ message: String) {
        guard seq == clock.target?.seq else { return }
        failures[monitor] = "\(name): \(message)"
        let text = String(message.prefix(256))
        for link in links where link.isPaired { link.send(.handoffFailed(seq: seq, identity: monitor, message: text)) }
    }

    // MARK: Discovery

    private func service() -> NWListener.Service {
        var txt = NWTXTRecord(["id": identity.keyID.hex, "pv": String(ProtocolMessage.currentVersion)])
        if isPairingMode { txt["pairing"] = "1" }
        return NWListener.Service(name: name, type: SecureChannel.serviceType, txtRecord: txt)
    }

    private func startBrowsing() {
        let browser = NWBrowser(for: .bonjourWithTXTRecord(type: SecureChannel.serviceType, domain: nil), using: .tcp)
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            MainActor.assumeIsolated { self?.update(results) }
        }
        browser.start(queue: .main)
        self.browser = browser
    }

    private func update(_ results: Set<NWBrowser.Result>) {
        var found: [String: Discovered] = [:]
        for result in results {
            guard case let .bonjour(txt) = result.metadata, let id = txt["id"], id != identity.keyID.hex,
                  case let .service(name, _, _, _) = result.endpoint
            else { continue }
            found[id] = Discovered(endpoint: result.endpoint, name: name, isPairable: txt["pairing"] == "1",
                                   since: discovered[id]?.since ?? Date())
        }
        discovered = found
        publish()
    }

    private func publish() {
        var all: [String: Peer] = [:]
        for (id, peer) in discovered {
            all[id] = Peer(id: id, name: peer.name, platform: nil, isPaired: false, isConnected: false,
                           isPairable: peer.isPairable, monitors: [])
        }
        for (id, peer) in paired {
            all[id, default: Peer(id: id, name: peer.name, platform: nil, isPaired: true, isConnected: false, isPairable: false, monitors: [])]
                .isPaired = true
            all[id]?.name = peer.name
            all[id]?.platform = peer.platform
        }
        for link in links where link.isPaired {
            guard let id = link.peerID else { continue }
            all[id]?.isConnected = true
            all[id]?.monitors = peerMonitors[id] ?? []
        }
        peers = all.values.sorted { ($0.name, $0.id) < ($1.name, $1.id) }
    }
}
