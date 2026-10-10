import Foundation
import Network
import ScreenFerryKit
import Testing
@testable import ScreenFerryNet

@MainActor
struct AgentTests {
    static func store() -> PairedPeerStore {
        PairedPeerStore(url: FileManager.default.temporaryDirectory
            .appending(path: "screenferry-tests-\(UUID().uuidString)/paired-peers.json"))
    }

    static func agent(_ name: String, identity: AgentIdentity? = nil, store: PairedPeerStore = store()) throws -> Agent {
        let agent = try Agent(identity: identity ?? .ephemeral(), name: name, appVersion: "0.2.1", store: store)
        try agent.start(advertise: false)
        return agent
    }

    static func eventually(_ what: Comment, timeout: Duration = .seconds(10), _ condition: () -> Bool) async throws {
        let deadline = ContinuousClock.now + timeout
        while !condition() {
            guard ContinuousClock.now < deadline else {
                Issue.record("Timed out waiting for: \(what)")
                return
            }
            try await Task.sleep(for: .milliseconds(20))
        }
    }

    static func endpoint(_ agent: Agent) async throws -> NWEndpoint {
        try await eventually("listener port") { agent.listenerPort.map { $0 > 0 } ?? false }
        return .hostPort(host: "127.0.0.1", port: NWEndpoint.Port(rawValue: agent.listenerPort!)!)
    }

    static func connected(_ agent: Agent, to other: Agent) -> Agent.Peer? {
        agent.peers.first { $0.id == other.identity.keyID.hex && $0.isConnected }
    }

    @Test func pairsShowsMonitorsAndReconnects() async throws {
        let macStore = Self.store(), pcStore = Self.store()
        let macIdentity = try AgentIdentity.ephemeral(), pcIdentity = try AgentIdentity.ephemeral()
        var mac = try Self.agent("Mac", identity: macIdentity, store: macStore)
        var pc = try Self.agent("PC", identity: pcIdentity, store: pcStore)

        pc.setPairingMode(true)
        mac.pair(with: try await Self.endpoint(pc))
        try await Self.eventually("codes shown") { mac.pairingPrompt != nil && pc.pairingPrompt != nil }
        #expect(mac.pairingPrompt?.code == pc.pairingPrompt?.code)
        #expect(mac.pairingPrompt?.peerName == "PC")
        #expect(pc.pairingPrompt?.peerName == "Mac")

        pc.confirmPairing(true)
        mac.confirmPairing(true)
        try await Self.eventually("paired and connected") { Self.connected(mac, to: pc) != nil && Self.connected(pc, to: mac) != nil }
        #expect(!mac.isPairingMode && !pc.isPairingMode)
        #expect(try macStore.load().map(\.keyID) == [pcIdentity.keyID.hex])

        let g8 = ProtocolMessage.MonitorStatus(identity: "SAM-E030-H1AK500000", name: "Odyssey G80SD", attached: true)
        pc.setLocalMonitors([g8])
        try await Self.eventually("monitors arrive") { Self.connected(mac, to: pc)?.monitors == [g8] }

        mac.stop()
        pc.stop()
        mac = try Self.agent("Mac", identity: macIdentity, store: macStore)
        pc = try Self.agent("PC", identity: pcIdentity, store: pcStore)
        pc.setLocalMonitors([g8])
        mac.connect(to: try await Self.endpoint(pc))
        try await Self.eventually("reconnected without pairing") { Self.connected(mac, to: pc)?.monitors == [g8] }
        #expect(mac.pairingPrompt == nil)
        mac.stop()
        pc.stop()
    }

    @Test func rejectionStoresNothing() async throws {
        let mac = try Self.agent("Mac"), pc = try Self.agent("PC")
        pc.setPairingMode(true)
        mac.pair(with: try await Self.endpoint(pc))
        try await Self.eventually("codes shown") { mac.pairingPrompt != nil && pc.pairingPrompt != nil }
        pc.confirmPairing(false)
        try await Self.eventually("both gave up") { mac.pairingPrompt == nil && pc.pairingPrompt == nil }
        #expect(try mac.store.load().isEmpty && pc.store.load().isEmpty)
        #expect(mac.pairingResult?.contains("rejectedByPeer") == true)
        mac.stop()
        pc.stop()
    }

    @Test func unpairedPeerIsRefusedOutsidePairingMode() async throws {
        let mac = try Self.agent("Mac"), pc = try Self.agent("PC")
        mac.pair(with: try await Self.endpoint(pc))
        try await Task.sleep(for: .seconds(1))
        #expect(mac.pairingPrompt == nil && pc.pairingPrompt == nil)
        #expect(mac.peers.isEmpty && pc.peers.isEmpty)
        mac.stop()
        pc.stop()
    }
}
