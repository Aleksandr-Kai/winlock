using System.Text.Json;

namespace WinLock.ControllerStub;

public sealed record StubConfig(string ServerUrl, string ControllerDisplayName)
{
    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "stub-config.json");

    public static StubConfig LoadOrCreateDefault()
    {
        if (File.Exists(ConfigPath))
        {
            var json = File.ReadAllText(ConfigPath);
            var loaded = JsonSerializer.Deserialize<StubConfig>(json, JsonOptions);
            if (loaded is not null)
                return loaded;
        }

        // No IP/mDNS discovery yet (see the MVP note) — the PC's address is just written
        // here by hand for now.
        var defaultConfig = new StubConfig("https://localhost:51843", "Test Stub");
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(defaultConfig, JsonOptions));
        return defaultConfig;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
