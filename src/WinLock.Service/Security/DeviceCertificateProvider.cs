using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WinLock.Service.Security;

/// <summary>
/// Every PC gets its own long-lived self-signed certificate for the local HTTPS/WebSocket
/// channel. There's no CA involved and none is needed: a paired phone doesn't validate this
/// cert's chain at all — it pins the exact certificate by its SHA-256 fingerprint, which
/// rides along in the pairing QR (see <c>PairingQrPayload</c>). That out-of-band QR transfer
/// *is* the trust anchor, the same way it is for the shared secret itself; a network
/// attacker who doesn't control the screen the QR was read from can't substitute their own
/// certificate even once, let alone silently.
///
/// The certificate (with its private key) is kept as PKCS#12 bytes inside the same
/// DPAPI-protected state file as everything else, rather than a platform certificate store —
/// simpler, and it means this runs the same way on every OS instead of only Windows.
/// </summary>
public static class DeviceCertificateProvider
{
    private const string SubjectName = "CN=WinLock Agent";

    /// <summary>Loads the previously persisted certificate if there is one and it's still
    /// valid, otherwise generates a fresh one (10-year validity) and returns its PFX bytes
    /// for the caller to persist. Reusing the same certificate across restarts matters:
    /// regenerating it would silently invalidate every phone's pin.</summary>
    public static (X509Certificate2 Certificate, byte[] PfxBytes) GetOrCreate(byte[]? persistedPfx)
    {
        if (persistedPfx is { Length: > 0 })
        {
            var existing = new X509Certificate2(persistedPfx, (string?)null, X509KeyStorageFlags.Exportable);
            if (existing.NotAfter > DateTime.UtcNow.AddDays(30))
                return (existing, persistedPfx);
        }

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(SubjectName, ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], critical: false)); // server authentication

        var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        var pfxBytes = ephemeral.Export(X509ContentType.Pfx);
        var persistable = new X509Certificate2(pfxBytes, (string?)null, X509KeyStorageFlags.Exportable);
        return (persistable, pfxBytes);
    }

    public static string ComputeFingerprintHex(X509Certificate2 certificate) =>
        Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
}
