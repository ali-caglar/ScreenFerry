import Foundation
import ScreenFerryKit
import Testing
@testable import ScreenFerryNet

@MainActor
final class FakeDisplays: HandoffExecutor {
    weak var agent: Agent?
    var monitors: [ProtocolMessage.MonitorStatus]
    var steps: [HandoffStep] = []
    var identities: [String] = []
    var failing: Error?

    init(_ agent: Agent, _ monitors: [ProtocolMessage.MonitorStatus]) {
        self.agent = agent
        self.monitors = monitors
        agent.executor = self
        agent.settle = 0.2
        agent.setLocalMonitors(monitors)
    }

    func perform(_ step: HandoffStep, on identity: String) async throws {
        steps.append(step)
        identities.append(identity)
        if let failing { throw failing }
        guard let index = monitors.firstIndex(where: { $0.identity == identity }) else { return }
        switch step {
        case .attach: monitors[index].attached = true
        case .switchAndRelease, .releaseAfterSettle: monitors[index].attached = false
        default: break
        }
        agent?.setLocalMonitors(monitors)
    }
}

struct Refusal: Error, CustomStringConvertible {
    var description: String { "no" }
}

@MainActor
struct HandoffAgentTests {
    static let g8 = "SAM-E030-H1AK500000"

    static func pairedAgents() async throws -> (Agent, Agent) {
        let mac = try AgentTests.agent("Mac"), pc = try AgentTests.agent("PC")
        pc.setPairingMode(true)
        mac.pair(with: try await AgentTests.endpoint(pc))
        try await AgentTests.eventually("codes shown") { mac.pairingPrompt != nil && pc.pairingPrompt != nil }
        pc.confirmPairing(true)
        mac.confirmPairing(true)
        try await AgentTests.eventually("connected") { AgentTests.connected(mac, to: pc) != nil && AgentTests.connected(pc, to: mac) != nil }
        return (mac, pc)
    }

    static func done(_ agents: Agent...) -> Bool {
        agents.allSatisfy { $0.handoff?.pending.isEmpty == true }
    }

    @Test func autoSourceAttachesFirstThenReleases() async throws {
        let (mac, pc) = try await Self.pairedAgents()
        let macDisplays = FakeDisplays(mac, [.init(identity: Self.g8, attached: true)])
        let pcDisplays = FakeDisplays(pc, [.init(identity: Self.g8, attached: false)])
        try await AgentTests.eventually("monitors exchanged") { AgentTests.connected(mac, to: pc)?.monitors.count == 1 }

        mac.activate(owners: [Self.g8: pc.keyID])
        try await AgentTests.eventually("moved to the PC") { Self.done(mac, pc) }
        #expect(pcDisplays.steps == [.attach])
        #expect(macDisplays.steps == [.releaseAfterSettle])
        #expect(pc.handoff?.seq == 1)

        pc.activate(owners: [Self.g8: mac.keyID])
        try await AgentTests.eventually("moved back") { Self.done(mac, pc) && mac.handoff?.seq == 2 }
        #expect(macDisplays.monitors[0].attached && !pcDisplays.monitors[0].attached)
        mac.stop()
        pc.stop()
    }

    @Test func leavingHandsAttachedMonitorsToAPeer() async throws {
        let (mac, pc) = try await Self.pairedAgents()
        let macDisplays = FakeDisplays(mac, [.init(identity: Self.g8, attached: true, inputCode: 17)])
        let pcDisplays = FakeDisplays(pc, [.init(identity: Self.g8, attached: false, inputCode: 15)])
        try await AgentTests.eventually("monitors exchanged") { AgentTests.connected(mac, to: pc)?.monitors.count == 1 }

        #expect(mac.handOffBeforeLeaving())
        try await AgentTests.eventually("attached on the PC") { mac.handoffReachedTargets }
        #expect(macDisplays.steps == [.switchAndRelease(inputCode: 15)])
        #expect(pcDisplays.steps == [.attach])
        mac.stop()
        pc.stop()
    }

    @Test func ddcSwitchesThenAttaches() async throws {
        let (mac, pc) = try await Self.pairedAgents()
        let macDisplays = FakeDisplays(mac, [.init(identity: Self.g8, attached: true, inputCode: 17)])
        let pcDisplays = FakeDisplays(pc, [.init(identity: Self.g8, attached: false, inputCode: 15)])
        try await AgentTests.eventually("monitors exchanged") { AgentTests.connected(pc, to: mac)?.monitors.count == 1 }

        pc.activate(owners: [Self.g8: pc.keyID])
        try await AgentTests.eventually("moved to the PC") { Self.done(mac, pc) }
        #expect(macDisplays.steps == [.switchAndRelease(inputCode: 15)])
        #expect(pcDisplays.steps == [.attach])
        mac.stop()
        pc.stop()
    }

    @Test func matchesAMonitorWithAnotherIdentityPerInput() async throws {
        let (mac, pc) = try await Self.pairedAgents()
        let macDisplays = FakeDisplays(mac, [.init(identity: "SAM-E030-H1AK500000", name: "Odyssey G80SD", attached: true)])
        let pcDisplays = FakeDisplays(pc, [.init(identity: "SAM-E035-H1AK500000", name: "Odyssey G80SD", attached: false)])
        try await AgentTests.eventually("monitors exchanged") { AgentTests.connected(mac, to: pc)?.monitors.count == 1 }

        mac.activate(owners: ["SAM-E030-H1AK500000": pc.keyID])
        try await AgentTests.eventually("moved to the PC") { Self.done(mac, pc) }
        #expect(pcDisplays.identities == ["SAM-E035-H1AK500000"])
        #expect(macDisplays.identities == ["SAM-E030-H1AK500000"])
        mac.stop()
        pc.stop()
    }

    @Test func failureReachesThePeer() async throws {
        let (mac, pc) = try await Self.pairedAgents()
        _ = FakeDisplays(mac, [.init(identity: Self.g8, attached: true)])
        let pcDisplays = FakeDisplays(pc, [.init(identity: Self.g8, attached: false)])
        pcDisplays.failing = Refusal()
        try await AgentTests.eventually("monitors exchanged") { AgentTests.connected(mac, to: pc)?.monitors.count == 1 }

        mac.activate(owners: [Self.g8: pc.keyID])
        try await AgentTests.eventually("failure reported") { mac.handoff?.failures[Self.g8] == "PC: no" }
        #expect(mac.handoff?.isFinished == true)
        mac.stop()
        pc.stop()
    }
}
