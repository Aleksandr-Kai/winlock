using WinLock.Core.Models;
using WinLock.Core.Network;

namespace WinLock.Service;

/// <summary>Default until the network hub is wired in, and the fallback on non-Windows builds.</summary>
public sealed class NullStatusPublisher : IAgentStatusPublisher
{
    public Task PublishAsync(LockDecision decision, CancellationToken ct = default) => Task.CompletedTask;
}
