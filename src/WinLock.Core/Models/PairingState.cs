using System.Security.Cryptography;

namespace WinLock.Core.Models;

/// <summary>
/// This PC's identity and the set of parent apps ("controllers") paired to it. A single
/// child machine can be supervised by several parents at once (each independently paired,
/// each independently revocable), which is why this is a list rather than one secret.
/// </summary>
public sealed class PairingState
{
    public Guid DeviceId { get; set; } = Guid.NewGuid();

    /// <summary>Shown to parents during pairing so they can tell machines apart, e.g. "Ноутбук Саши".</summary>
    public string DeviceDisplayName { get; set; } = string.Empty;

    /// <summary>Used only to sign the (non-secret) offline-unlock QR payload for integrity —
    /// see <see cref="Offline.OfflineUnlockService"/>. Generated once, independent of any
    /// particular controller, so it exists even before the device is paired to anyone.</summary>
    public byte[] IntegritySecret { get; set; } = RandomNumberGenerator.GetBytes(32);

    public List<PairedController> Controllers { get; set; } = new();

    public bool IsPaired => Controllers.Count > 0;

    /// <summary>This device's persisted HTTPS certificate (PKCS#12/PFX bytes, see
    /// <c>DeviceCertificateProvider</c>), remembered so the same certificate — and thus the
    /// same pin every paired phone already trusts — survives a service restart. Lives inside
    /// the same DPAPI-protected state file as everything else, so it needs no separate
    /// protection or platform-specific certificate store.</summary>
    public byte[]? CertificatePfx { get; set; }
}
