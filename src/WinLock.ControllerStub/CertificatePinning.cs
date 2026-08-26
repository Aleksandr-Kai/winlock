using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace WinLock.ControllerStub;

/// <summary>
/// The PC's certificate is self-signed on purpose — there's no CA, and this is why: instead
/// of validating a chain, a real controller pins the exact certificate by the SHA-256
/// fingerprint carried in the pairing QR. Anything else — a MITM, a spoofed device on the
/// same LAN — presents a different certificate and gets rejected outright.
/// </summary>
public static class CertificatePinning
{
    public static bool Validate(string expectedFingerprintHex, X509Certificate? certificate)
    {
        if (certificate is null)
            return false;

        var actual = Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
        return string.Equals(actual, expectedFingerprintHex, StringComparison.OrdinalIgnoreCase);
    }
}
