using WinLock.Core.Models;

namespace WinLock.Core.State;

public interface IStateStore
{
    Task<AgentPersistedData> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AgentPersistedData data, CancellationToken ct = default);
}
