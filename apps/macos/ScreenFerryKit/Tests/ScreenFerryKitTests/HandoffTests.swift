import Foundation
import Testing
@testable import ScreenFerryKit

struct HandoffTests {
    struct Vectors: Decodable {
        struct Case: Decodable, CustomTestStringConvertible {
            struct State: Decodable {
                let attached: Bool
                let inputCode: Int?
            }

            struct Expected: Decodable {
                let step: String
                let done: Bool
                let inputCode: Int?
            }

            let description: String
            let local: String
            let target: String
            let states: [String: State]
            let expected: Expected
            var testDescription: String { description }

            enum CodingKeys: String, CodingKey {
                case description, local = "self", target, states, expected
            }
        }

        let cases: [Case]
    }

    static let vectors: Vectors = {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .appending(path: "../../../../../protocol/test-vectors/handoff.json").standardizedFileURL
        return try! JSONDecoder().decode(Vectors.self, from: Data(contentsOf: url))
    }()

    static func name(_ step: HandoffStep) -> (String, Int?) {
        switch step {
        case .none: ("none", nil)
        case .attach: ("attach", nil)
        case .waitForRelease: ("waitForRelease", nil)
        case let .switchAndRelease(code): ("switchAndRelease", code)
        case .waitForAttach: ("waitForAttach", nil)
        case .releaseAfterSettle: ("releaseAfterSettle", nil)
        case .missing: ("missing", nil)
        }
    }

    @Test(arguments: vectors.cases)
    func matchesVector(_ v: Vectors.Case) {
        let states = v.states.mapValues { Handoff.State(attached: $0.attached, inputCode: $0.inputCode) }
        let (step, code) = Self.name(Handoff.step(local: v.local, target: v.target, states: states))
        #expect(step == v.expected.step)
        #expect(code == v.expected.inputCode)
        #expect(Handoff.isDone(target: v.target, states: states) == v.expected.done)
    }
}

struct MonitorMatchTests {
    struct Vectors: Decodable {
        struct Case: Decodable, CustomTestStringConvertible {
            let description: String
            let reference: ProtocolMessage.MonitorStatus
            let candidates: [ProtocolMessage.MonitorStatus]
            let match: String?
            var testDescription: String { description }
        }

        let cases: [Case]
    }

    static let vectors: Vectors = {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .appending(path: "../../../../../protocol/test-vectors/monitor-matching.json").standardizedFileURL
        // The vectors omit `attached`; default it.
        var json = try! JSONSerialization.jsonObject(with: Data(contentsOf: url)) as! [String: Any]
        json["cases"] = (json["cases"] as! [[String: Any]]).map { item in
            var item = item
            func fill(_ m: [String: Any]) -> [String: Any] { m.merging(["attached": false]) { a, _ in a } }
            item["reference"] = fill(item["reference"] as! [String: Any])
            item["candidates"] = (item["candidates"] as! [[String: Any]]).map(fill)
            return item
        }
        return try! JSONDecoder().decode(Vectors.self, from: JSONSerialization.data(withJSONObject: json))
    }()

    @Test(arguments: vectors.cases)
    func matchesVector(_ v: Vectors.Case) {
        #expect(MonitorMatch.find(v.reference, in: v.candidates)?.identity == v.match)
    }
}

struct HandoffClockTests {
    @Test func newestWins() {
        var clock = HandoffClock()
        let mine = clock.start(owners: ["G8": "a"], origin: "a")
        #expect(mine.seq == 1)
        let accepted = [
            clock.receive(seq: 1, owners: ["G8": "b"], from: "a"),
            clock.receive(seq: 1, owners: ["G8": "b"], from: "b"),
            clock.receive(seq: 1, owners: ["G8": "a"], from: "a"),
            clock.receive(seq: 4, owners: ["G8": "a"], from: "a"),
        ]
        #expect(accepted == [false, true, false, true])
        #expect(clock.target?.owners == ["G8": "a"])
        let next = clock.start(owners: [:], origin: "b")
        #expect(next.seq == 5)
    }
}
