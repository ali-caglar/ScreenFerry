import CryptoKit
import Foundation
import Testing
@testable import ScreenFerryKit

struct PairingTests {
    struct Vectors: Decodable {
        struct Key: Decodable, CustomTestStringConvertible {
            let description: String
            let publicKey: String
            let keyId: String
            var testDescription: String { description }
        }

        struct Exchange: Decodable, CustomTestStringConvertible {
            let description: String
            let initiatorKeyId, responderKeyId, initiatorNonce, responderNonce, commitment, code: String
            var testDescription: String { description }
        }

        let keyIds: [Key]
        let pairing: [Exchange]
    }

    static let vectors: Vectors = {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
            .appending(path: "../../../../../protocol/test-vectors/pairing.json").standardizedFileURL
        return try! JSONDecoder().decode(Vectors.self, from: Data(contentsOf: url))
    }()

    static func hex(_ text: String) -> Data { Data(hex: text)! }

    @Test(arguments: vectors.keyIds)
    func keyIDMatchesVector(_ vector: Vectors.Key) throws {
        let key = try P256.Signing.PublicKey(x963Representation: Self.hex(vector.publicKey))
        #expect(Pairing.keyID(key).hex == vector.keyId)
    }

    @Test(arguments: vectors.pairing)
    func exchangeMatchesVector(_ v: Vectors.Exchange) {
        #expect(Pairing.commitment(responderNonce: Self.hex(v.responderNonce), responderKeyID: Self.hex(v.responderKeyId),
                                   initiatorKeyID: Self.hex(v.initiatorKeyId)).hex == v.commitment)
        #expect(Pairing.code(initiatorKeyID: Self.hex(v.initiatorKeyId), responderKeyID: Self.hex(v.responderKeyId),
                             initiatorNonce: Self.hex(v.initiatorNonce), responderNonce: Self.hex(v.responderNonce)) == v.code)
    }

    static let i = Data(repeating: 1, count: 32)
    static let r = Data(repeating: 2, count: 32)

    static func sessions() -> (PairingSession, PairingSession) {
        (PairingSession(role: .initiator, localKeyID: i, peerKeyID: r), PairingSession(role: .responder, localKeyID: r, peerKeyID: i))
    }

    static func sent(_ events: [PairingSession.Event]) -> [ProtocolMessage] {
        events.compactMap { if case let .send(m) = $0 { m } else { nil } }
    }

    static func code(_ events: [PairingSession.Event]) -> String? {
        events.lazy.compactMap { if case let .showCode(c) = $0 { c } else { nil } }.first
    }

    @Test func bothSidesShowTheSameCodeAndPair() {
        var (initiator, responder) = Self.sessions()
        #expect(initiator.start().isEmpty)
        let commit = Self.sent(responder.start())
        let initiatorNonce = Self.sent(initiator.receive(commit[0]))
        let responderEvents = responder.receive(initiatorNonce[0])
        let initiatorEvents = initiator.receive(Self.sent(responderEvents)[0])
        #expect(Self.code(initiatorEvents) != nil)
        #expect(Self.code(initiatorEvents) == Self.code(responderEvents))

        let fromResponder = Self.sent(responder.userDecided(true))
        #expect(initiator.receive(fromResponder[0]).isEmpty)
        let initiatorDone = initiator.userDecided(true)
        #expect(initiatorDone.last == .paired)
        #expect(responder.receive(Self.sent(initiatorDone)[0]) == [.paired])
    }

    @Test func wrongCommitmentFails() {
        var (initiator, responder) = Self.sessions()
        _ = initiator.start()
        _ = responder.start()
        let nonce = Self.sent(initiator.receive(.pairCommit(commitment: Data(repeating: 0, count: 32))))
        let events = initiator.receive(Self.sent(responder.receive(nonce[0]))[0])
        #expect(events.last == .failed(.commitmentMismatch))
        #expect(Self.code(events) == nil)
    }

    @Test func responderMustCommitFirst() {
        var (initiator, _) = Self.sessions()
        _ = initiator.start()
        #expect(initiator.receive(.pairNonce(Self.r)).last == .failed(.outOfOrder))
    }

    @Test func confirmBeforeCodeFails() {
        var (_, responder) = Self.sessions()
        _ = responder.start()
        #expect(responder.receive(.pairConfirm(accepted: true)).last == .failed(.outOfOrder))
        #expect(responder.userDecided(true) == [.failed(.outOfOrder)])
    }

    @Test func rejectionOnEitherSideFails() {
        var (initiator, responder) = Self.sessions()
        _ = initiator.start()
        let nonce = Self.sent(initiator.receive(Self.sent(responder.start())[0]))
        _ = initiator.receive(Self.sent(responder.receive(nonce[0]))[0])
        let rejection = responder.userDecided(false)
        #expect(rejection == [.send(.pairConfirm(accepted: false)), .failed(.rejectedHere)])
        #expect(initiator.receive(Self.sent(rejection)[0]) == [.failed(.rejectedByPeer)])
    }
}
