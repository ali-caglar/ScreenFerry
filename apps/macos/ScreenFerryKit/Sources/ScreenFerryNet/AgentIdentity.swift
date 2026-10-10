import CryptoKit
import Foundation
import ScreenFerryKit
import Security
import SwiftASN1
import X509

public enum AgentIdentityError: Error {
    case certificate, key(CFError?), identity, keychain(OSStatus)
}

/// This agent's long-term P-256 key (ADR 0006) and a self-signed certificate for TLS, made fresh at each launch.
public struct AgentIdentity: @unchecked Sendable {
    public let privateKey: P256.Signing.PrivateKey
    public let keyID: Data
    let secIdentity: SecIdentity

    public init(privateKey: P256.Signing.PrivateKey) throws {
        let keyID = Pairing.keyID(privateKey.publicKey)
        let name = try DistinguishedName { CommonName("ScreenFerry \(keyID.prefix(8).hex)") }
        let now = Date()
        let certificate = try Certificate(
            version: .v3,
            serialNumber: Certificate.SerialNumber(),
            publicKey: Certificate.PublicKey(privateKey.publicKey),
            notValidBefore: now.addingTimeInterval(-86_400),
            notValidAfter: now.addingTimeInterval(20 * 365 * 86_400),
            issuer: name,
            subject: name,
            signatureAlgorithm: .ecdsaWithSHA256,
            extensions: try Certificate.Extensions {
                Critical(BasicConstraints.notCertificateAuthority)
                Critical(KeyUsage(digitalSignature: true))
                try ExtendedKeyUsage([.serverAuth, .clientAuth])
            },
            issuerPrivateKey: Certificate.PrivateKey(privateKey))
        var serializer = DER.Serializer()
        try serializer.serialize(certificate)
        guard let secCertificate = SecCertificateCreateWithData(nil, Data(serializer.serializedBytes) as CFData) else {
            throw AgentIdentityError.certificate
        }

        var error: Unmanaged<CFError>?
        let attributes = [kSecAttrKeyType: kSecAttrKeyTypeECSECPrimeRandom, kSecAttrKeyClass: kSecAttrKeyClassPrivate] as CFDictionary
        guard let secKey = SecKeyCreateWithData(privateKey.x963Representation as CFData, attributes, &error) else {
            throw AgentIdentityError.key(error?.takeRetainedValue())
        }
        guard let identity = SecIdentityCreate(nil, secCertificate, secKey) else { throw AgentIdentityError.identity }
        self.privateKey = privateKey
        self.keyID = keyID
        secIdentity = identity
    }

    public static func ephemeral() throws -> AgentIdentity {
        try AgentIdentity(privateKey: P256.Signing.PrivateKey())
    }

    /// Loads the key from the login keychain, creating it on first use.
    public static func keychain(account: String = "default") throws -> AgentIdentity {
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: "ScreenFerry agent key",
            kSecAttrAccount: account,
        ]
        var item: CFTypeRef?
        let status = SecItemCopyMatching(query.merging([kSecReturnData: true]) { $1 } as CFDictionary, &item)
        if status == errSecSuccess, let data = item as? Data {
            return try AgentIdentity(privateKey: P256.Signing.PrivateKey(rawRepresentation: data))
        }
        guard status == errSecItemNotFound else { throw AgentIdentityError.keychain(status) }

        let key = P256.Signing.PrivateKey()
        let added = SecItemAdd(query.merging([
            kSecValueData: key.rawRepresentation,
            kSecAttrLabel: "ScreenFerry agent key",
        ]) { $1 } as CFDictionary, nil)
        guard added == errSecSuccess else { throw AgentIdentityError.keychain(added) }
        return try AgentIdentity(privateKey: key)
    }
}
