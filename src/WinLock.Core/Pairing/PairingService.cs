using System.Security.Cryptography;
using System.Text;
using WinLock.Core.Models;

namespace WinLock.Core.Pairing;

/// <summary>
/// Runs the pairing ceremony: an administrator on the PC puts the device into pairing mode
/// (showing a QR), a parent scans it with their phone, and the phone proves it read the
/// live QR by posting back the one-time token embedded in it — over the network, but
/// without ever sending the secret itself, which both sides already agreed on purely by the
/// phone reading the screen. One PC can be paired to several parents this way, each
/// independently, each later independently revocable.
/// </summary>
public sealed class PairingService
{
    private static readonly Encoding Utf8 = Encoding.UTF8;

    private readonly object _gate = new();
    private readonly PairingState _pairing;
    private PendingPairing? _pending;

    public PairingService(PairingState pairing)
    {
        _pairing = pairing;
    }

    /// <summary>Puts the device into pairing mode and returns what to encode as the QR.
    /// Starting a new pairing attempt discards any still-pending one. The certificate
    /// fingerprint and address are the network layer's concern, not this service's — they're
    /// passed in so the QR can carry everything a phone needs in one scan.</summary>
    public PairingQrPayload BeginPairing(TimeSpan validity, string certificateFingerprintHex, string hostAndPort)
    {
        lock (_gate)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            var secret = RandomNumberGenerator.GetBytes(32);
            _pending = new PendingPairing(token, secret, DateTimeOffset.UtcNow + validity);
            return new PairingQrPayload(
                _pairing.DeviceId, _pairing.DeviceDisplayName, token, secret, certificateFingerprintHex, hostAndPort);
        }
    }

    public void CancelPairing()
    {
        lock (_gate)
            _pending = null;
    }

    /// <summary>Called when a phone posts back a token — over the network channel, after
    /// discovering the PC. Success means that phone genuinely read the currently displayed
    /// QR (nobody else could have known the token), so it's added as a new controller.</summary>
    public bool TryCompletePairing(string token, string controllerDisplayName, out Guid controllerId)
    {
        lock (_gate)
        {
            controllerId = default;

            if (_pending is null)
                return false;

            if (DateTimeOffset.UtcNow > _pending.ExpiresAtUtc)
            {
                _pending = null;
                return false;
            }

            if (!CryptographicOperations.FixedTimeEquals(Utf8.GetBytes(_pending.Token), Utf8.GetBytes(token)))
                return false;

            var controller = new PairedController
            {
                DisplayName = string.IsNullOrWhiteSpace(controllerDisplayName) ? "Родительское приложение" : controllerDisplayName,
                SharedSecret = _pending.Secret,
                PairedAtUtc = DateTimeOffset.UtcNow,
            };
            _pairing.Controllers.Add(controller);
            controllerId = controller.ControllerId;
            _pending = null; // one-shot: this token can never pair a second device
            return true;
        }
    }

    /// <summary>Removes one parent's access. Everyone else paired to this PC is unaffected.</summary>
    public bool RevokeController(Guid controllerId)
    {
        lock (_gate)
            return _pairing.Controllers.RemoveAll(c => c.ControllerId == controllerId) > 0;
    }

    private sealed record PendingPairing(string Token, byte[] Secret, DateTimeOffset ExpiresAtUtc);
}
