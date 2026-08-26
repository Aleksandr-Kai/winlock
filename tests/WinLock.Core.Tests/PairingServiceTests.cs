using WinLock.Core.Models;
using WinLock.Core.Pairing;

namespace WinLock.Core.Tests;

public class PairingServiceTests
{
    [Fact]
    public void TryCompletePairing_Succeeds_WithTheTokenFromTheQr()
    {
        var pairing = new PairingState();
        var service = new PairingService(pairing);
        var qr = service.BeginPairing(TimeSpan.FromMinutes(5), "fp", "127.0.0.1:5000");

        var success = service.TryCompletePairing(qr.Token, "Mom's phone", out var controllerId);

        Assert.True(success);
        Assert.NotEqual(Guid.Empty, controllerId);
        var added = Assert.Single(pairing.Controllers);
        Assert.Equal(controllerId, added.ControllerId);
        Assert.Equal("Mom's phone", added.DisplayName);
        Assert.Equal(qr.Secret, added.SharedSecret);
    }

    [Fact]
    public void TryCompletePairing_Fails_WithWrongToken()
    {
        var pairing = new PairingState();
        var service = new PairingService(pairing);
        service.BeginPairing(TimeSpan.FromMinutes(5), "fp", "127.0.0.1:5000");

        Assert.False(service.TryCompletePairing("not-the-real-token", "Attacker", out _));
        Assert.Empty(pairing.Controllers);
    }

    [Fact]
    public void TryCompletePairing_IsOneShot()
    {
        var pairing = new PairingState();
        var service = new PairingService(pairing);
        var qr = service.BeginPairing(TimeSpan.FromMinutes(5), "fp", "127.0.0.1:5000");

        Assert.True(service.TryCompletePairing(qr.Token, "First", out _));
        Assert.False(service.TryCompletePairing(qr.Token, "Second", out _));
        Assert.Single(pairing.Controllers);
    }

    [Fact]
    public void TryCompletePairing_Fails_WhenNoPairingWasEverStarted()
    {
        var service = new PairingService(new PairingState());
        Assert.False(service.TryCompletePairing("anything", "X", out _));
    }

    [Fact]
    public void BeginPairing_Twice_InvalidatesTheFirstToken()
    {
        var pairing = new PairingState();
        var service = new PairingService(pairing);
        var first = service.BeginPairing(TimeSpan.FromMinutes(5), "fp", "127.0.0.1:5000");
        service.BeginPairing(TimeSpan.FromMinutes(5), "fp", "127.0.0.1:5000");

        Assert.False(service.TryCompletePairing(first.Token, "Late scanner", out _));
    }

    [Fact]
    public void SeveralParents_CanEachPairIndependently()
    {
        var pairing = new PairingState();
        var service = new PairingService(pairing);

        var qr1 = service.BeginPairing(TimeSpan.FromMinutes(5), "fp", "127.0.0.1:5000");
        service.TryCompletePairing(qr1.Token, "Mom", out _);

        var qr2 = service.BeginPairing(TimeSpan.FromMinutes(5), "fp", "127.0.0.1:5000");
        service.TryCompletePairing(qr2.Token, "Dad", out _);

        Assert.Equal(2, pairing.Controllers.Count);
        Assert.Contains(pairing.Controllers, c => c.DisplayName == "Mom");
        Assert.Contains(pairing.Controllers, c => c.DisplayName == "Dad");
        Assert.NotEqual(qr1.Secret, qr2.Secret);
    }

    [Fact]
    public void RevokeController_RemovesOnlyThatController()
    {
        var pairing = new PairingState();
        var service = new PairingService(pairing);
        service.TryCompletePairing(service.BeginPairing(TimeSpan.FromMinutes(1), "fp", "127.0.0.1:5000").Token, "Mom", out var momId);
        service.TryCompletePairing(service.BeginPairing(TimeSpan.FromMinutes(1), "fp", "127.0.0.1:5000").Token, "Dad", out _);

        Assert.True(service.RevokeController(momId));

        var remaining = Assert.Single(pairing.Controllers);
        Assert.Equal("Dad", remaining.DisplayName);
    }

    [Fact]
    public void CancelPairing_InvalidatesTheOutstandingToken()
    {
        var pairing = new PairingState();
        var service = new PairingService(pairing);
        var qr = service.BeginPairing(TimeSpan.FromMinutes(5), "fp", "127.0.0.1:5000");

        service.CancelPairing();

        Assert.False(service.TryCompletePairing(qr.Token, "Too late", out _));
    }
}
