namespace WinLock.Core.Models;

/// <summary>Everything the agent needs to keep enforcing the schedule across a reboot, offline.</summary>
public sealed class AgentPersistedData
{
    public ScheduleConfig Schedule { get; set; } = new();
    public UsageState Usage { get; set; } = new();
    public PairingState Pairing { get; set; } = new();
    public OfflineUnlockState Offline { get; set; } = new();
}
