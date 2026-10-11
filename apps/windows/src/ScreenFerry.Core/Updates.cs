using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace ScreenFerry.Core;

/// <param name="Signature">Base64 ECDSA P-256 / SHA-256 signature (DER) over the zip.</param>
public sealed record UpdateAsset(
    [property: JsonPropertyName("url")] Uri Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("signature")] string Signature);

/// <summary><c>windows-update.json</c> in a release (ADR 0009). <paramref name="Assets"/> is keyed by runtime ID.</summary>
public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("assets")] IReadOnlyDictionary<string, UpdateAsset> Assets);

public static class UpdatePolicy
{
    /// <summary>A release X.Y.0 is newer than A.B.C if X.Y > A.B, or X.Y = A.B and C ≠ 0 (a pre-release build, ADR 0004).</summary>
    public static bool IsNewer(string release, string current)
    {
        if (!TryParse(release, out var r) || r.Patch != 0 || !TryParse(current, out var c))
        {
            return false;
        }
        return (r.Major, r.Minor).CompareTo((c.Major, c.Minor)) > 0 || ((r.Major, r.Minor) == (c.Major, c.Minor) && c.Patch != 0);
    }

    /// <param name="publicKey">SubjectPublicKeyInfo (DER) of the update key.</param>
    public static bool Verify(byte[] data, UpdateAsset asset, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(data)), asset.Sha256, StringComparison.Ordinal))
        {
            return false;
        }
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out _);
            return key.VerifyData(data, Convert.FromBase64String(asset.Signature), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryParse(string text, out (int Major, int Minor, int Patch) version)
    {
        version = default;
        var parts = text.Split('+')[0].Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }
        version = (major, minor, patch);
        return true;
    }
}
