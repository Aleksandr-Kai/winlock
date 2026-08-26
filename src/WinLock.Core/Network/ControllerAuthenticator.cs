using System.Security.Cryptography;
using System.Text;
using WinLock.Core.Models;

namespace WinLock.Core.Network;

/// <summary>
/// Authenticates an already-paired controller (phone) opening a live network connection.
/// A fresh, random nonce per connection plus an HMAC response means the shared secret
/// itself is never sent over the network — not at pairing time, not here, not ever — and a
/// captured auth message can't be replayed against a later connection.
/// </summary>
public sealed class ControllerAuthenticator
{
    private readonly PairingState _pairing;

    public ControllerAuthenticator(PairingState pairing)
    {
        _pairing = pairing;
    }

    public static string GenerateNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    public static string ComputeAuthResponse(byte[] controllerSecret, string nonce)
    {
        using var hmac = new HMACSHA256(controllerSecret);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(nonce)));
    }

    /// <summary>Returns the authenticated controller, or null if the id is unknown or the
    /// response doesn't match that controller's secret for this nonce.</summary>
    public PairedController? TryAuthenticate(string nonce, Guid controllerId, string authResponseBase64)
    {
        var controller = _pairing.Controllers.FirstOrDefault(c => c.ControllerId == controllerId);
        if (controller is null)
            return null;

        byte[] provided;
        try
        {
            provided = Convert.FromBase64String(authResponseBase64);
        }
        catch (FormatException)
        {
            return null;
        }

        var expected = Convert.FromBase64String(ComputeAuthResponse(controller.SharedSecret, nonce));
        return CryptographicOperations.FixedTimeEquals(expected, provided) ? controller : null;
    }
}
