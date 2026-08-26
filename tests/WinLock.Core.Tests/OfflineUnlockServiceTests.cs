using System.Security.Cryptography;
using WinLock.Core.Models;
using WinLock.Core.Offline;

namespace WinLock.Core.Tests;

public class OfflineUnlockServiceTests
{
    private static (OfflineUnlockService service, PairingState pairing, byte[] secret) BuildPaired()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var pairing = new PairingState
        {
            Controllers = { new PairedController { DisplayName = "Test Parent", SharedSecret = secret } },
        };
        var service = new OfflineUnlockService(pairing, new OfflineUnlockState());
        return (service, pairing, secret);
    }

    [Fact]
    public void IssueChallenge_ThrowsWhenNotPaired()
    {
        var service = new OfflineUnlockService(new PairingState(), new OfflineUnlockState());
        Assert.Throws<InvalidOperationException>(() => service.IssueChallenge());
    }

    [Fact]
    public void TryRedeem_Succeeds_WithCorrectCodeForTheChallengeAndMinutes()
    {
        var (service, _, secret) = BuildPaired();
        var challenge = service.IssueChallenge();
        var code = OfflineUnlockService.ComputeResponseCode(secret, challenge.ChallengeId, minutes: 30);

        Assert.True(service.TryRedeem(challenge.ChallengeId, 30, code));
    }

    [Fact]
    public void TryRedeem_Fails_WhenCodeWasComputedForDifferentMinutes()
    {
        // A parent-approved code for "+15 min" must not also unlock "+120 min": the code
        // has to be bound to the specific grant, not just to the challenge.
        var (service, _, secret) = BuildPaired();
        var challenge = service.IssueChallenge();
        var codeFor15 = OfflineUnlockService.ComputeResponseCode(secret, challenge.ChallengeId, minutes: 15);

        Assert.False(service.TryRedeem(challenge.ChallengeId, 120, codeFor15));
    }

    [Fact]
    public void TryRedeem_Fails_ForAnUnissuedOrStaleChallengeId()
    {
        var (service, _, secret) = BuildPaired();
        var challenge = service.IssueChallenge();
        var code = OfflineUnlockService.ComputeResponseCode(secret, challenge.ChallengeId, minutes: 30);

        Assert.False(service.TryRedeem(challenge.ChallengeId + 999, 30, code));
    }

    [Fact]
    public void TryRedeem_IsOneShot_SameCodeCannotBeReplayed()
    {
        var (service, _, secret) = BuildPaired();
        var challenge = service.IssueChallenge();
        var code = OfflineUnlockService.ComputeResponseCode(secret, challenge.ChallengeId, minutes: 30);

        Assert.True(service.TryRedeem(challenge.ChallengeId, 30, code));
        Assert.False(service.TryRedeem(challenge.ChallengeId, 30, code));
    }

    [Fact]
    public void IssueChallenge_InvalidatesAnyPreviouslyOutstandingChallenge()
    {
        var (service, _, secret) = BuildPaired();
        var first = service.IssueChallenge();
        var codeForFirst = OfflineUnlockService.ComputeResponseCode(secret, first.ChallengeId, minutes: 30);

        service.IssueChallenge(); // a fresh QR is shown; the old one is no longer valid

        Assert.False(service.TryRedeem(first.ChallengeId, 30, codeForFirst));
    }

    [Fact]
    public void TryRedeem_LocksOutTheChallenge_AfterMaxFailedAttempts()
    {
        var (service, _, secret) = BuildPaired();
        var challenge = service.IssueChallenge();
        var correctCode = OfflineUnlockService.ComputeResponseCode(secret, challenge.ChallengeId, minutes: 30);

        for (var i = 0; i < OfflineUnlockState.MaxAttempts; i++)
            Assert.False(service.TryRedeem(challenge.ChallengeId, 30, "0000"));

        // Even the genuinely correct code no longer works: the challenge was burned by
        // the repeated wrong guesses, so brute-forcing a 4-digit code isn't free.
        Assert.False(service.TryRedeem(challenge.ChallengeId, 30, correctCode));
    }

    [Fact]
    public void TryRedeem_Fails_WithoutMutatingState_WhenNotPaired()
    {
        var service = new OfflineUnlockService(new PairingState(), new OfflineUnlockState());
        Assert.False(service.TryRedeem(1, 30, "1234"));
    }

    [Fact]
    public void DifferentSecrets_ProduceDifferentCodes_ForTheSameChallengeAndMinutes()
    {
        var secretA = RandomNumberGenerator.GetBytes(32);
        var secretB = RandomNumberGenerator.GetBytes(32);

        var codeA = OfflineUnlockService.ComputeResponseCode(secretA, 42, 30);
        var codeB = OfflineUnlockService.ComputeResponseCode(secretB, 42, 30);

        Assert.NotEqual(codeA, codeB);
    }

    [Fact]
    public void TryRedeem_AcceptsACode_FromAnyOneOfSeveralPairedParents()
    {
        var secretMom = RandomNumberGenerator.GetBytes(32);
        var secretDad = RandomNumberGenerator.GetBytes(32);
        var pairing = new PairingState
        {
            Controllers =
            {
                new PairedController { DisplayName = "Mom", SharedSecret = secretMom },
                new PairedController { DisplayName = "Dad", SharedSecret = secretDad },
            },
        };
        var service = new OfflineUnlockService(pairing, new OfflineUnlockState());
        var challenge = service.IssueChallenge();

        var dadsCode = OfflineUnlockService.ComputeResponseCode(secretDad, challenge.ChallengeId, 20);

        Assert.True(service.TryRedeem(challenge.ChallengeId, 20, dadsCode));
    }
}
