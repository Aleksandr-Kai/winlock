using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using WinLock.Core.Models;

namespace WinLock.Core.Offline;

/// <summary>
/// Lets any paired parent unlock the machine (or grant extra minutes) with zero network on
/// either side. The lock screen shows a QR challenge; a paired phone (holding its own
/// pairing secret) computes a short response code offline and the parent reads it out to
/// whoever is at the PC.
///
/// Security note: the QR payload itself carries no secret and needs none — it's just an
/// unpredictable, single-use id. Everything rests on the response code, which only someone
/// holding one of the paired <see cref="PairedController.SharedSecret"/> values can compute
/// for that id, and which the PC only accepts once, for the one challenge it actually
/// issued. A leaked or photographed QR code is useless without a paired phone's secret.
/// </summary>
public sealed class OfflineUnlockService
{
    // 4 digits, not 6: same tradeoff as a debit card PIN — a small, easy-to-type-under-
    // pressure code, kept safe by OfflineUnlockState.MaxAttempts burning the challenge
    // after a handful of wrong guesses rather than by the code space alone.
    private const int ResponseCodeDigits = 4;
    private static readonly Encoding Utf8 = Encoding.UTF8;

    private readonly object _gate = new();
    private readonly PairingState _pairing;
    private readonly OfflineUnlockState _state;

    public OfflineUnlockService(PairingState pairing, OfflineUnlockState state)
    {
        _pairing = pairing;
        _state = state;
    }

    /// <summary>Issues a fresh challenge for display as a QR code, invalidating whatever
    /// challenge (and attempt count) was outstanding before.</summary>
    public OfflineUnlockChallenge IssueChallenge()
    {
        lock (_gate)
        {
            RequirePaired();

            var id = _state.NextChallengeId++;
            _state.OutstandingChallengeId = id;
            _state.FailedAttempts = 0;

            var tag = ComputeIntegrityTag(id);
            return new OfflineUnlockChallenge(id, _pairing.DeviceId, tag);
        }
    }

    /// <summary>The canonical response-code algorithm, given any one controller's secret.
    /// Public and static so a paired phone (and tests) can compute the same value
    /// independently, without needing a whole <see cref="OfflineUnlockService"/> instance.</summary>
    public static string ComputeResponseCode(byte[] controllerSecret, long challengeId, int minutes)
    {
        Span<byte> input = stackalloc byte[8 + 4];
        BinaryPrimitives.WriteInt64BigEndian(input[..8], challengeId);
        BinaryPrimitives.WriteInt32BigEndian(input.Slice(8, 4), minutes);

        using var hmac = new HMACSHA256(controllerSecret);
        var hash = hmac.ComputeHash(input.ToArray());
        return TruncateToDigits(hash, ResponseCodeDigits);
    }

    /// <summary>Validates a response code typed at the PC against every paired controller's
    /// secret — any one of the parents may be the one who answers. On success the challenge
    /// is consumed immediately; callers must apply the extension themselves right after.</summary>
    public bool TryRedeem(long challengeId, int minutes, string code)
    {
        lock (_gate)
        {
            if (!_pairing.IsPaired) return false;
            if (minutes <= 0) return false;
            if (_state.OutstandingChallengeId != challengeId) return false;

            if (_state.FailedAttempts >= OfflineUnlockState.MaxAttempts)
            {
                _state.OutstandingChallengeId = null; // burn it: force a fresh QR scan
                return false;
            }

            var trimmed = code.Trim();
            var isMatch = trimmed.Length == ResponseCodeDigits
                          && _pairing.Controllers.Any(controller => CryptographicOperations.FixedTimeEquals(
                              Utf8.GetBytes(ComputeResponseCode(controller.SharedSecret, challengeId, minutes)),
                              Utf8.GetBytes(trimmed)));

            if (!isMatch)
            {
                _state.FailedAttempts++;
                return false;
            }

            _state.OutstandingChallengeId = null;
            _state.FailedAttempts = 0;
            return true;
        }
    }

    private void RequirePaired()
    {
        if (!_pairing.IsPaired)
            throw new InvalidOperationException("Device is not paired with any parent app yet.");
    }

    private string ComputeIntegrityTag(long challengeId)
    {
        Span<byte> input = stackalloc byte[16 + 8];
        _pairing.DeviceId.TryWriteBytes(input[..16]);
        BinaryPrimitives.WriteInt64BigEndian(input.Slice(16, 8), challengeId);

        using var hmac = new HMACSHA256(_pairing.IntegritySecret);
        var hash = hmac.ComputeHash(input.ToArray());
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    // RFC 4226-style dynamic truncation: turns an HMAC digest into a short decimal code.
    private static string TruncateToDigits(byte[] hmac, int digits)
    {
        var offset = hmac[^1] & 0x0F;
        var binCode = ((hmac[offset] & 0x7f) << 24)
                      | ((hmac[offset + 1] & 0xff) << 16)
                      | ((hmac[offset + 2] & 0xff) << 8)
                      | (hmac[offset + 3] & 0xff);
        var mod = (int)Math.Pow(10, digits);
        return (binCode % mod).ToString(new string('0', digits));
    }
}
