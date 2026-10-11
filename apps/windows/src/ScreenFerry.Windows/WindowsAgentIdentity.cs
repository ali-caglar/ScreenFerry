using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ScreenFerry.Core;

namespace ScreenFerry.Windows;

[SupportedOSPlatform("windows")]
public static class WindowsAgentIdentity
{
    private const string FriendlyName = "ScreenFerry agent";

    /// <summary>The agent's certificate in the user's certificate store, with a non-exportable CNG key; created on first use.</summary>
    public static AgentIdentity LoadOrCreate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        foreach (var existing in store.Certificates)
        {
            if (existing.FriendlyName == FriendlyName && existing.HasPrivateKey)
            {
                return AgentIdentity.FromCertificate(existing);
            }
            existing.Dispose();
        }

        using var key = CngKey.Create(CngAlgorithm.ECDsaP256, "ScreenFerry agent key", new CngKeyCreationParameters
        {
            ExportPolicy = CngExportPolicies.None,
            KeyCreationOptions = CngKeyCreationOptions.OverwriteExistingKey,
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
        });
        using var ecdsa = new ECDsaCng(key);
        var certificate = AgentIdentity.CreateCertificate(ecdsa);
        certificate.FriendlyName = FriendlyName;
        store.Add(certificate);
        return AgentIdentity.FromCertificate(certificate);
    }
}
