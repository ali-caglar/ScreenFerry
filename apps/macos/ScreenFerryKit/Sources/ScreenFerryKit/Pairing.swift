import CryptoKit
import Foundation

/// Key IDs and the numeric-comparison pairing exchange of ADR 0006.
public enum Pairing {
    /// SHA-256 of the DER SubjectPublicKeyInfo of a P-256 key.
    public static func keyID(_ publicKey: P256.Signing.PublicKey) -> Data {
        Data(SHA256.hash(data: publicKey.derRepresentation))
    }

    public static func commitment(responderNonce: Data, responderKeyID: Data, initiatorKeyID: Data) -> Data {
        let message = Data("ScreenFerry pair commit v1".utf8) + responderKeyID + initiatorKeyID
        return Data(HMAC<SHA256>.authenticationCode(for: message, using: SymmetricKey(data: responderNonce)))
    }

    /// Six digits, zero-padded.
    public static func code(initiatorKeyID: Data, responderKeyID: Data, initiatorNonce: Data, responderNonce: Data) -> String {
        let input = Data("ScreenFerry pair code v1".utf8) + initiatorKeyID + responderKeyID + initiatorNonce + responderNonce
        let value = Data(SHA256.hash(data: input)).prefix(4).reduce(UInt32(0)) { $0 << 8 | UInt32($1) }
        return String(format: "%06u", value % 1_000_000)
    }

    public static func newNonce() -> Data {
        SymmetricKey(size: .bits256).withUnsafeBytes { Data($0) }
    }
}

/// One pairing attempt on one connection. Feed it the peer's `pair.*` messages and the user's decision;
/// it enforces the order and says what to send and show.
public struct PairingSession: Sendable {
    public enum Role: Sendable {
        case initiator, responder
    }

    public enum Failure: Error, Equatable, Sendable {
        case outOfOrder, commitmentMismatch, rejectedHere, rejectedByPeer
    }

    public enum Event: Equatable, Sendable {
        case send(ProtocolMessage)
        case showCode(String)
        case paired
        case failed(Failure)
    }

    private enum State: Equatable {
        case idle, awaitingCommit, awaitingInitiatorNonce, awaitingResponderNonce(commitment: Data)
        case deciding(local: Bool?, peer: Bool?)
        case done
    }

    public let role: Role
    private let initiatorKeyID: Data
    private let responderKeyID: Data
    private let nonce: Data
    private var state = State.idle

    /// Key IDs are the ones observed in the TLS handshake.
    public init(role: Role, localKeyID: Data, peerKeyID: Data, nonce: Data = Pairing.newNonce()) {
        self.role = role
        initiatorKeyID = role == .initiator ? localKeyID : peerKeyID
        responderKeyID = role == .initiator ? peerKeyID : localKeyID
        self.nonce = nonce
    }

    public mutating func start() -> [Event] {
        guard state == .idle else { return fail(.outOfOrder) }
        switch role {
        case .initiator:
            state = .awaitingCommit
            return []
        case .responder:
            state = .awaitingInitiatorNonce
            let commitment = Pairing.commitment(responderNonce: nonce, responderKeyID: responderKeyID, initiatorKeyID: initiatorKeyID)
            return [.send(.pairCommit(commitment: commitment))]
        }
    }

    public mutating func receive(_ message: ProtocolMessage) -> [Event] {
        switch (state, message) {
        case let (.awaitingCommit, .pairCommit(commitment)):
            state = .awaitingResponderNonce(commitment: commitment)
            return [.send(.pairNonce(nonce))]
        case let (.awaitingInitiatorNonce, .pairNonce(initiatorNonce)):
            state = .deciding(local: nil, peer: nil)
            return [.send(.pairNonce(nonce)), .showCode(code(initiatorNonce: initiatorNonce, responderNonce: nonce))]
        case let (.awaitingResponderNonce(commitment), .pairNonce(responderNonce)):
            let expected = Pairing.commitment(responderNonce: responderNonce, responderKeyID: responderKeyID, initiatorKeyID: initiatorKeyID)
            guard expected == commitment else { return fail(.commitmentMismatch) }
            state = .deciding(local: nil, peer: nil)
            return [.showCode(code(initiatorNonce: nonce, responderNonce: responderNonce))]
        case let (.deciding(local, nil), .pairConfirm(accepted)):
            guard accepted else {
                state = .done
                return [.failed(.rejectedByPeer)]
            }
            return decide(local: local, peer: true)
        default:
            return fail(.outOfOrder)
        }
    }

    public mutating func userDecided(_ accepted: Bool) -> [Event] {
        guard case let .deciding(nil, peer) = state else { return fail(.outOfOrder) }
        guard accepted else {
            state = .done
            return [.send(.pairConfirm(accepted: false)), .failed(.rejectedHere)]
        }
        return [.send(.pairConfirm(accepted: true))] + decide(local: true, peer: peer)
    }

    private mutating func decide(local: Bool?, peer: Bool?) -> [Event] {
        if local == true, peer == true {
            state = .done
            return [.paired]
        }
        state = .deciding(local: local, peer: peer)
        return []
    }

    private func code(initiatorNonce: Data, responderNonce: Data) -> String {
        Pairing.code(initiatorKeyID: initiatorKeyID, responderKeyID: responderKeyID,
                     initiatorNonce: initiatorNonce, responderNonce: responderNonce)
    }

    private mutating func fail(_ failure: Failure) -> [Event] {
        let wasDone = state == .done
        state = .done
        if wasDone { return [.failed(.outOfOrder)] }
        return [.send(.error(.pairingFailed)), .failed(failure)]
    }
}
