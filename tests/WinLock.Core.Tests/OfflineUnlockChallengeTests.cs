using WinLock.Core.Offline;

namespace WinLock.Core.Tests;

public class OfflineUnlockChallengeTests
{
    [Fact]
    public void ToQrText_ThenTryParse_RoundTrips()
    {
        var original = new OfflineUnlockChallenge(42, Guid.NewGuid(), "deadbeef");

        var ok = OfflineUnlockChallenge.TryParse(original.ToQrText(), out var parsed);

        Assert.True(ok);
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-winlock-qr")]
    [InlineData("winlock:v2:abc:1:tag")]
    [InlineData("winlock:v1:not-a-guid:1:tag")]
    [InlineData("winlock:v1:00000000000000000000000000000000:not-a-number:tag")]
    public void TryParse_RejectsMalformedInput(string input)
    {
        Assert.False(OfflineUnlockChallenge.TryParse(input, out _));
    }
}
