import CryptoKit
import Foundation
import Network
import ScreenFerryKit
import Security

/// Mutually authenticated TLS as in ADR 0006: no CA, the peer is trusted only by its key ID.
enum SecureChannel {
    static let serviceType = "_screenferry._tcp"

    /// `accept` decides on the peer's key ID during the handshake.
    static func parameters(identity: AgentIdentity, queue: DispatchQueue, accept: @escaping @Sendable (Data) -> Bool) -> NWParameters {
        let tls = NWProtocolTLS.Options()
        let options = tls.securityProtocolOptions
        sec_protocol_options_set_local_identity(options, sec_identity_create(identity.secIdentity)!)
        // TLS 1.2 only for Windows 10, which lacks 1.3; then ECDHE + AEAD only.
        sec_protocol_options_set_min_tls_protocol_version(options, .TLSv12)
        for suite: tls_ciphersuite_t in [
            .AES_128_GCM_SHA256, .AES_256_GCM_SHA384, .CHACHA20_POLY1305_SHA256,
            .ECDHE_ECDSA_WITH_AES_128_GCM_SHA256, .ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
            .ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
        ] {
            sec_protocol_options_append_tls_ciphersuite(options, suite)
        }
        sec_protocol_options_set_peer_authentication_required(options, true)
        sec_protocol_options_set_verify_block(options, { _, trust, complete in
            let chain = SecTrustCopyCertificateChain(sec_trust_copy_ref(trust).takeRetainedValue()) as? [SecCertificate]
            complete(chain?.first.flatMap(keyID).map(accept) ?? false)
        }, queue)

        let tcp = NWProtocolTCP.Options()
        tcp.enableKeepalive = true
        tcp.connectionTimeout = 10
        return NWParameters(tls: tls, tcp: tcp)
    }

    static func keyID(_ certificate: SecCertificate) -> Data? {
        guard let key = SecCertificateCopyKey(certificate),
              let data = SecKeyCopyExternalRepresentation(key, nil) as Data?,
              let publicKey = try? P256.Signing.PublicKey(x963Representation: data)
        else { return nil }
        return Pairing.keyID(publicKey)
    }

    static func peerKeyID(_ connection: NWConnection) -> Data? {
        guard let metadata = connection.metadata(definition: NWProtocolTLS.definition) as? NWProtocolTLS.Metadata else { return nil }
        var keyID: Data?
        sec_protocol_metadata_access_peer_certificate_chain(metadata.securityProtocolMetadata) { certificate in
            if keyID == nil { keyID = Self.keyID(sec_certificate_copy_ref(certificate).takeRetainedValue()) }
        }
        return keyID
    }
}

/// Framed protocol messages over one TLS connection. All callbacks run on `queue`.
final class PeerConnection: @unchecked Sendable {
    enum Event {
        case ready(peerKeyID: Data)
        case message(ProtocolMessage)
        case closed(String?)
    }

    let connection: NWConnection
    let outgoing: Bool
    private let queue: DispatchQueue
    private var framer = MessageFramer()
    private var onEvent: (@Sendable (Event) -> Void)?
    private(set) var lastReceived = Date()

    init(connection: NWConnection, outgoing: Bool, queue: DispatchQueue) {
        self.connection = connection
        self.outgoing = outgoing
        self.queue = queue
    }

    func start(_ onEvent: @escaping @Sendable (Event) -> Void) {
        self.onEvent = onEvent
        connection.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            switch state {
            case .ready:
                guard let keyID = SecureChannel.peerKeyID(connection) else { return close("No peer certificate.") }
                self.onEvent?(.ready(peerKeyID: keyID))
                receive()
            case let .failed(error), let .waiting(error):
                close("\(error)")
            case .cancelled:
                close(nil)
            default:
                break
            }
        }
        connection.start(queue: queue)
    }

    func send(_ message: ProtocolMessage) {
        guard let frame = try? MessageFramer.frame(message) else { return }
        connection.send(content: frame, completion: .contentProcessed { [weak self] error in
            if let error { self?.close("\(error)") }
        })
    }

    func close(_ reason: String?) {
        guard let onEvent else { return }
        self.onEvent = nil
        connection.cancel()
        onEvent(.closed(reason))
    }

    private func receive() {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 65_536) { [weak self] data, _, isComplete, error in
            guard let self, onEvent != nil else { return }
            if let data, !data.isEmpty {
                lastReceived = Date()
                do {
                    for frame in try framer.append(data) {
                        onEvent?(.message(try JSONDecoder().decode(ProtocolMessage.self, from: frame)))
                    }
                } catch {
                    send(.error(.badMessage))
                    return close("Bad message: \(error)")
                }
            }
            if let error { return close("\(error)") }
            if isComplete { return close(nil) }
            receive()
        }
    }
}
