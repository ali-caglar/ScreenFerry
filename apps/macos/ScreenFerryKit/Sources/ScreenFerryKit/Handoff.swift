import Foundation

/// What one agent does next for one monitor of a handoff (ADR 0007, `protocol/test-vectors/handoff.json`).
public enum HandoffStep: Equatable, Sendable {
    case none
    case attach
    case waitForRelease
    /// Set VCP `0x60` to `inputCode`, then release.
    case switchAndRelease(inputCode: Int)
    case waitForAttach
    case releaseAfterSettle
    /// This agent is the target but doesn't see the monitor.
    case missing
}

public enum Handoff {
    public struct State: Equatable, Sendable {
        public var attached: Bool
        public var inputCode: Int?

        public init(attached: Bool, inputCode: Int? = nil) {
            self.attached = attached
            self.inputCode = inputCode
        }
    }

    public static let expiry: Double = 120
    public static let settle: Double = 5

    /// `states`: what each connected agent, including `local`, reports for the monitor.
    public static func step(local: String, target: String, states: [String: State]) -> HandoffStep {
        let owners = states.filter { $0.key != target && $0.value.attached }
        let targetCode = states[target]?.inputCode
        let isDDC = !owners.isEmpty && targetCode != nil && owners.values.allSatisfy { $0.inputCode != nil }
        if local == target {
            guard let state = states[local] else { return .missing }
            if state.attached { return .none }
            return isDDC ? .waitForRelease : .attach
        }
        guard owners[local] != nil else { return .none }
        if isDDC, let targetCode { return .switchAndRelease(inputCode: targetCode) }
        return states[target]?.attached == true ? .releaseAfterSettle : .waitForAttach
    }

    /// Attached on the target and nowhere else.
    public static func isDone(target: String, states: [String: State]) -> Bool {
        states[target]?.attached == true && !states.contains { $0.key != target && $0.value.attached }
    }
}

/// The handoff an agent is working towards.
public struct HandoffTarget: Equatable, Sendable {
    public var seq: Int
    /// Key ID (hex) of the agent that started it.
    public var origin: String
    /// Monitor identity → owner key ID (hex).
    public var owners: [String: String]
    public var startedAt: Date
}

/// Lamport ordering of handoffs: the newest `(seq, origin)` wins (ADR 0007).
public struct HandoffClock: Sendable {
    public private(set) var highestSeq = 0
    public private(set) var target: HandoffTarget?

    public init() {}

    public mutating func start(owners: [String: String], origin: String, now: Date = .now) -> HandoffTarget {
        highestSeq += 1
        let target = HandoffTarget(seq: highestSeq, origin: origin, owners: owners, startedAt: now)
        self.target = target
        return target
    }

    /// Returns whether the received handoff replaced the current target.
    public mutating func receive(seq: Int, owners: [String: String], from origin: String, now: Date = .now) -> Bool {
        highestSeq = max(highestSeq, seq)
        if let target, (seq, origin) <= (target.seq, target.origin) { return false }
        target = HandoffTarget(seq: seq, origin: origin, owners: owners, startedAt: now)
        return true
    }
}
