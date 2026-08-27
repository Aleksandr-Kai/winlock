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

    [Fact]
    public async Task Load_CorruptFile_PreservesTheUnreadableBytes_RatherThanLettingTheNextSaveDestroyThem()
    {
        // Regression test: a silent fail-safe-to-blank-defaults used to mean the very next
        // SaveAsync would overwrite the only copy of a corrupt-but-maybe-still-useful file
        // with an empty state — losing the schedule, pairing, and certificate all at once,
        // indistinguishable from a genuinely fresh install and with zero trace of what
        // happened. The corrupt file must survive under a backup name, and a breadcrumb
        // explaining why must be left next to it.
        var path = Path.Combine(Path.GetTempPath(), $"winlock-test-{Guid.NewGuid():N}.json");
        const string corruptContent = "{ not valid json ][";
        await File.WriteAllTextAsync(path, corruptContent);
        var store = new JsonFileStateStore(path, new NullStateProtector());
        try
        {
            await store.LoadAsync();

            var directory = Path.GetDirectoryName(path)!;
            var fileName = Path.GetFileName(path);
            var backups = Directory.GetFiles(directory, $"{fileName}.corrupt-*");
            Assert.Single(backups);
            Assert.Equal(corruptContent, await File.ReadAllTextAsync(backups[0]));

            var errorLogPath = path + ".errors.log";
            Assert.True(File.Exists(errorLogPath));
            var logContents = await File.ReadAllTextAsync(errorLogPath);
            Assert.Contains("could not read/decrypt state", logContents);

            File.Delete(backups[0]);
            File.Delete(errorLogPath);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
