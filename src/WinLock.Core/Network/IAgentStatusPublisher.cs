using WinLock.Core.Models;

namespace WinLock.Core.Network;

/// <summary>Pushed to every currently connected, authenticated controller after each
/// evaluation cycle, so every parent's app stays live-updated without polling.</summary>
public interface IAgentStatusPublisher
{
    Task PublishAsync(LockDecision decision, CancellationToken ct = default);
}
