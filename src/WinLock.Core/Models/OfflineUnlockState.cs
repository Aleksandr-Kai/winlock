namespace WinLock.Core.Models;

/// <summary>Persisted bookkeeping for the offline (no-network) QR unlock flow.</summary>
public sealed class OfflineUnlockState
{
    /// <summary>Monotonically increasing counter; never reused, even across restarts,
    /// so an old QR code (and the response code computed for it) can never be replayed.</summary>
    public long NextChallengeId { get; set; } = 1;

    /// <summary>The one challenge currently valid for redemption, if any. Issuing a new
    /// challenge — or a successful redemption — invalidates whatever was here before.</summary>
    public long? OutstandingChallengeId { get; set; }

    /// <summary>Wrong-code attempts against the current outstanding challenge. The
    /// challenge is invalidated once this hits <see cref="MaxAttempts"/>, forcing a fresh
    /// QR scan rather than letting a response code be brute-forced against a static challenge.</summary>
    public int FailedAttempts { get; set; }

    public const int MaxAttempts = 5;
}
