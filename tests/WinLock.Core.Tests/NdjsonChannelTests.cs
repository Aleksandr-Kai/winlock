using WinLock.Core.Ipc;
using WinLock.Core.Models;

namespace WinLock.Core.Tests;

public class NdjsonChannelTests
{
    [Fact]
    public async Task ServiceToUiMessages_RoundTrip_ThroughAStream_PreservingConcreteType()
    {
        await using var stream = new MemoryStream();
        var writer = new NdjsonChannel<UiToServiceMessage, ServiceToUiMessage>(stream);

        await writer.WriteAsync(new LockCommand(LockReason.BudgetExhausted));
        await writer.WriteAsync(new ChallengeIssued(42, "winlock:v1:abc:42:deadbeef"));
        await writer.WriteAsync(new UnlockCommand());

        stream.Position = 0;
        var reader = new NdjsonChannel<ServiceToUiMessage, UiToServiceMessage>(stream);

        var first = await reader.ReadAsync();
        var second = await reader.ReadAsync();
        var third = await reader.ReadAsync();
        var fourth = await reader.ReadAsync();

        var lockMsg = Assert.IsType<LockCommand>(first);
        Assert.Equal(LockReason.BudgetExhausted, lockMsg.Reason);

        var challengeMsg = Assert.IsType<ChallengeIssued>(second);
        Assert.Equal(42, challengeMsg.ChallengeId);
        Assert.Equal("winlock:v1:abc:42:deadbeef", challengeMsg.QrText);

        Assert.IsType<UnlockCommand>(third);
        Assert.Null(fourth); // end of stream
    }

    [Fact]
    public async Task UiToServiceMessages_RoundTrip_ThroughAStream()
    {
        await using var stream = new MemoryStream();
        var writer = new NdjsonChannel<ServiceToUiMessage, UiToServiceMessage>(stream);

        await writer.WriteAsync(new RequestChallenge());
        await writer.WriteAsync(new RedeemOfflineUnlock(7, 30, "1234"));

        stream.Position = 0;
        var reader = new NdjsonChannel<UiToServiceMessage, ServiceToUiMessage>(stream);

        Assert.IsType<RequestChallenge>(await reader.ReadAsync());
        var redeem = Assert.IsType<RedeemOfflineUnlock>(await reader.ReadAsync());
        Assert.Equal(7, redeem.ChallengeId);
        Assert.Equal(30, redeem.Minutes);
        Assert.Equal("1234", redeem.Code);
    }
}
