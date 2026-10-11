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
