using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ScreenFerry.Core;

/// <summary>This agent's long-term P-256 key (ADR 0006) with a self-signed certificate for TLS.</summary>
public sealed class AgentIdentity : IDisposable
{
    private AgentIdentity(X509Certificate2 certificate)
    {
        using var key = certificate.GetECDsaPublicKey() ?? throw new ArgumentException("Not an ECDSA certificate.", nameof(certificate));
        if (!certificate.HasPrivateKey)
        {
            throw new ArgumentException("The certificate has no private key.", nameof(certificate));
        }
        Certificate = certificate;
        KeyId = Pairing.KeyId(key);
    }

    public X509Certificate2 Certificate { get; }

    public byte[] KeyId { get; }

    public string KeyIdHex => Convert.ToHexStringLower(KeyId);

    public static AgentIdentity FromCertificate(X509Certificate2 certificate) => new(certificate);

    /// <summary>A new key held in memory only (tests, or until a platform store exists).</summary>
    public static AgentIdentity Ephemeral()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var certificate = CreateCertificate(key);
        // SChannel can't use a certificate whose key was never exported; a PKCS#12 round trip fixes that.
        return new(X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null));
    }

    /// <summary>A self-signed certificate for <paramref name="key"/>; only the key is ever trusted.</summary>
    public static X509Certificate2 CreateCertificate(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var keyId = Convert.ToHexStringLower(Pairing.KeyId(key));
        var request = new CertificateRequest($"CN=ScreenFerry {keyId[..16]}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], false));
        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddDays(-1), now.AddYears(20));
    }

    /// <summary>The key ID of a peer's certificate, or null unless it holds a P-256 key.</summary>
    public static byte[]? KeyIdOf(X509Certificate? certificate)
    {
        if (certificate is null)
        {
            return null;
        }
        using var typed = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        using var key = typed.GetECDsaPublicKey();
        return key?.KeySize == 256 ? Pairing.KeyId(key) : null;
    }

    public void Dispose() => Certificate.Dispose();
}
