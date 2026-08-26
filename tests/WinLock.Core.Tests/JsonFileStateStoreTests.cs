using WinLock.Core.Models;
using WinLock.Core.State;

namespace WinLock.Core.Tests;

public class JsonFileStateStoreTests
{
    [Fact]
    public async Task SaveThenLoad_RoundTripsData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winlock-test-{Guid.NewGuid():N}.json");
        var store = new JsonFileStateStore(path, new NullStateProtector());
        try
        {
            var data = new AgentPersistedData
            {
                Schedule = new ScheduleConfig { DailyLimitMinutes = 90 },
                Usage = new UsageState { RemainingBudget = TimeSpan.FromMinutes(42), IsLocked = true },
            };

            await store.SaveAsync(data);
            var loaded = await store.LoadAsync();

            Assert.Equal(90, loaded.Schedule.DailyLimitMinutes);
            Assert.Equal(TimeSpan.FromMinutes(42), loaded.Usage.RemainingBudget);
            Assert.True(loaded.Usage.IsLocked);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsFreshDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winlock-test-{Guid.NewGuid():N}.json");
        var store = new JsonFileStateStore(path, new NullStateProtector());

        var loaded = await store.LoadAsync();

        Assert.Equal(TimeSpan.Zero, loaded.Usage.RemainingBudget);
    }

    [Fact]
    public async Task Load_CorruptFile_FailsSafeToFreshDefaults_InsteadOfThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winlock-test-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, "{ not valid json ][");
        var store = new JsonFileStateStore(path, new NullStateProtector());
        try
        {
            var loaded = await store.LoadAsync();
            Assert.Equal(TimeSpan.Zero, loaded.Usage.RemainingBudget);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
