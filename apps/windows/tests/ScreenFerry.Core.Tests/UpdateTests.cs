using System.Security.Cryptography;
using ScreenFerry.Core;

namespace ScreenFerry.Core.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("0.3.0", "0.2.0", true)]
    [InlineData("0.3.0", "0.3.4", true)]
    [InlineData("0.3.0", "0.3.0", false)]
    [InlineData("0.3.0", "0.4.2", false)]
    [InlineData("1.0.0", "0.9.0", true)]
    [InlineData("0.3.1", "0.2.0", false)]
    [InlineData("0.3.0", "0.3.4+abc", true)]
    [InlineData("garbage", "0.2.0", false)]
    public void IsNewer(string release, string current, bool expected) =>
        Assert.Equal(expected, UpdatePolicy.IsNewer(release, current));

    [Fact]
    public void VerifiesSignatureAndHash()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        byte[] zip = [1, 2, 3, 4];
        var asset = new UpdateAsset(new Uri("https://example.invalid/a.zip"), Convert.ToHexStringLower(SHA256.HashData(zip)),
            Convert.ToBase64String(key.SignData(zip, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)));

        Assert.True(UpdatePolicy.Verify(zip, asset, publicKey));
        Assert.False(UpdatePolicy.Verify([1, 2, 3, 5], asset, publicKey));
        Assert.False(UpdatePolicy.Verify([1, 2, 3, 5], asset with { Sha256 = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3, 5])) }, publicKey));
        Assert.False(UpdatePolicy.Verify(zip, asset with { Signature = "not base64!" }, publicKey));
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(UpdatePolicy.Verify(zip, asset, other.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    public void VerifiesAnOpenSslSignature()
    {
        // Throwaway key; signed with `openssl dgst -sha256 -sign`, as the release workflow does.
        var publicKey = Convert.FromBase64String("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEPDYON4O6zo9yGPuJVktubQOglVByZGNyicQTORpEEr4SVpZE5NBA14EH6Heoa1sM89XSgPtCg1qcR+FOZxgmhw==");
        var asset = new UpdateAsset(
            new Uri("https://example.invalid/a.zip"),
            "832f1b35e3dbbc9f15d4b45df0273a5ea4c382722642f966d3f98b72feba17d5",
            "MEYCIQD+cCc5hX0wUN8AUlQ6bdghfIT619vlVthhcQff/6/oYgIhAKcr4MNlcY3gEsqtUt/BcvLgu2/caCmTfi/kkcKqwECz");
        Assert.True(UpdatePolicy.Verify("ScreenFerry update test"u8.ToArray(), asset, publicKey));
    }
}
