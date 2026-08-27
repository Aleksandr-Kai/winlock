using WinLock.Core.Models;
using WinLock.Core.Network;
using WinLock.Core.Offline;
using WinLock.Core.Pairing;

namespace WinLock.Core;

/// <summary>
/// Thread-safe façade around <see cref="UsageTracker"/>, <see cref="OfflineUnlockService"/>,
/// <see cref="PairingService"/> and <see cref="ControllerAuthenticator"/>. The polling loop
/// calls <see cref="Evaluate"/> on a timer while, concurrently, commands can arrive from any
/// number of connected controllers over the network, from the offline QR flow, or from a
/// pairing attempt in progress — all of it needs a consistent view of the same state.
/// </summary>
public sealed class AgentRuntime
{
    private readonly object _gate = new();
    private readonly UsageTracker _tracker;
    private readonly OfflineUnlockService _offlineUnlock;
    private readonly PairingService _pairingService;
    private readonly ControllerAuthenticator _authenticator;
    private ScheduleConfig _schedule;
    private readonly PairingState _pairing;
    private readonly OfflineUnlockState _offlineState;
    private StateRecoveryIncident? _pendingStateRecoveryIncident;

    public AgentRuntime(
        UsageTracker tracker,
        ScheduleConfig initialSchedule,
        PairingState pairing,
        OfflineUnlockState offlineState,
        StateRecoveryIncident? pendingStateRecoveryIncident = null)
    {
        _tracker = tracker;
        _schedule = initialSchedule;
        _pairing = pairing;
        _offlineState = offlineState;
        _pendingStateRecoveryIncident = pendingStateRecoveryIncident;
        _offlineUnlock = new OfflineUnlockService(pairing, offlineState);
        _pairingService = new PairingService(pairing);
        _authenticator = new ControllerAuthenticator(pairing);
    }

    public Guid DeviceId => _pairing.DeviceId;

    public string DeviceDisplayName
    {
        get { lock (_gate) return _pairing.DeviceDisplayName; }
        set { lock (_gate) _pairing.DeviceDisplayName = value; }
    }

    public LockDecision Evaluate()
    {
        lock (_gate)
            return _tracker.Evaluate();
    }

    public void ExtendTime(TimeSpan extra)
    {
        lock (_gate)
            _tracker.ExtendTime(extra);
    }

    /// <summary>Sets today's remaining budget to an exact value, instead of adding to it.</summary>
    public void SetRemainingTime(TimeSpan value)
    {
        lock (_gate)
            _tracker.SetRemainingBudget(value);
    }

    public void UpdateSchedule(ScheduleConfig schedule)
    {
        lock (_gate)
        {
            _schedule = schedule;
            _tracker.UpdateSchedule(schedule);
        }
    }

    /// <summary>For a newly connected controller (or another one, after a third party
    /// changed it) to see what's actually configured, rather than a locally-remembered guess.</summary>
    public ScheduleConfig CurrentSchedule
    {
        get { lock (_gate) return _schedule; }
    }

    /// <summary>An explicit "lock it now" from a parent — always succeeds.</summary>
    public void LockNow()
    {
        lock (_gate)
            _tracker.SetManualLock();
    }

    /// <summary>An explicit "unlock it now" from a parent — fails if the budget has since
    /// run out, rather than lifting the lock only for the schedule to immediately re-impose it.</summary>
    public bool TryUnlockNow()
    {
        lock (_gate)
            return _tracker.TryClearManualLock();
    }

    /// <summary>Set once, at startup, if the persisted state couldn't be read and the agent
    /// had to fall back to a fresh, empty one — see JsonFileStateStore. Sent to every
    /// controller on connect until a parent acknowledges it.</summary>
    public StateRecoveryIncident? PendingStateRecoveryIncident
    {
        get { lock (_gate) return _pendingStateRecoveryIncident; }
    }

    public void AcknowledgeStateRecoveryIncident()
    {
        lock (_gate)
            _pendingStateRecoveryIncident = null;
    }

    /// <summary>For display as a QR code on the lock screen.</summary>
    public OfflineUnlockChallenge IssueOfflineChallenge()
    {
        lock (_gate)
            return _offlineUnlock.IssueChallenge();
    }

    /// <summary>Verifies a response code read out by the parent and, only if valid, grants
    /// the extension in the same locked section — a failed verification changes nothing.</summary>
    public bool TryRedeemOfflineUnlock(long challengeId, int minutes, string code)
    {
        lock (_gate)
        {
            if (!_offlineUnlock.TryRedeem(challengeId, minutes, code))
                return false;

            _tracker.ExtendTime(TimeSpan.FromMinutes(minutes));
            return true;
        }
    }

    public PairingQrPayload BeginPairing(TimeSpan validity, string certificateFingerprintHex, string hostAndPort)
    {
        lock (_gate)
            return _pairingService.BeginPairing(validity, certificateFingerprintHex, hostAndPort);
    }

    public void CancelPairing()
    {
        lock (_gate)
            _pairingService.CancelPairing();
    }

    public bool TryCompletePairing(string token, string controllerDisplayName, out Guid controllerId)
    {
        lock (_gate)
            return _pairingService.TryCompletePairing(token, controllerDisplayName, out controllerId);
    }

    public bool RevokeController(Guid controllerId)
    {
        lock (_gate)
            return _pairingService.RevokeController(controllerId);
    }

    public IReadOnlyList<PairedController> Controllers
    {
        get { lock (_gate) return _pairing.Controllers.ToList(); }
    }

    /// <summary>Verifies a controller's live network auth handshake against its stored secret.</summary>
    public PairedController? TryAuthenticateController(string nonce, Guid controllerId, string responseBase64)
    {
        lock (_gate)
            return _authenticator.TryAuthenticate(nonce, controllerId, responseBase64);
    }

    public AgentPersistedData Snapshot()
    {
        lock (_gate)
            return new AgentPersistedData
            {
                Schedule = _schedule,
                Usage = _tracker.State,
                Pairing = _pairing,
                Offline = _offlineState,
                PendingStateRecoveryIncident = _pendingStateRecoveryIncident,
            };
    }
}
