using WinLock.Core.Models;
using WinLock.Core.Network;

namespace WinLock.Core.Tests;

public class ControllerAuthenticatorTests
{
    private static PairedController AddController(PairingState pairing, byte[] secret)
    {
        var controller = new PairedController { DisplayName = "Phone", SharedSecret = secret };
        pairing.Controllers.Add(controller);
        return controller;
    }

    [Fact]
    public void TryAuthenticate_Succeeds_WithCorrectResponse()
    {
        var secret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var pairing = new PairingState();
        var controller = AddController(pairing, secret);
        var authenticator = new ControllerAuthenticator(pairing);

        var nonce = ControllerAuthenticator.GenerateNonce();
        var response = ControllerAuthenticator.ComputeAuthResponse(secret, nonce);

        var result = authenticator.TryAuthenticate(nonce, controller.ControllerId, response);

        Assert.NotNull(result);
        Assert.Equal(controller.ControllerId, result!.ControllerId);
    }

    [Fact]
    public void TryAuthenticate_Fails_WithResponseComputedForADifferentNonce()
    {
        var secret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var pairing = new PairingState();
        var controller = AddController(pairing, secret);
        var authenticator = new ControllerAuthenticator(pairing);

        var staleResponse = ControllerAuthenticator.ComputeAuthResponse(secret, "old-nonce");

        var result = authenticator.TryAuthenticate("current-nonce", controller.ControllerId, staleResponse);

        Assert.Null(result);
    }

    [Fact]
    public void TryAuthenticate_Fails_ForUnknownControllerId()
    {
        var pairing = new PairingState();
        var authenticator = new ControllerAuthenticator(pairing);

        var result = authenticator.TryAuthenticate("nonce", Guid.NewGuid(), "irrelevant");

        Assert.Null(result);
    }

    [Fact]
    public void TryAuthenticate_Fails_WithAnotherControllersSecret()
    {
        var pairing = new PairingState();
        var real = AddController(pairing, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var impostorSecret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var authenticator = new ControllerAuthenticator(pairing);

        var nonce = ControllerAuthenticator.GenerateNonce();
        var forgedResponse = ControllerAuthenticator.ComputeAuthResponse(impostorSecret, nonce);

        Assert.Null(authenticator.TryAuthenticate(nonce, real.ControllerId, forgedResponse));
    }
}
