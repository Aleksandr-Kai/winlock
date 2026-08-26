using System.Security.Cryptography;
using WinLock.Core.Pairing;

namespace WinLock.Core.Tests;

public class PairingQrPayloadTests
{
    [Fact]
    public void ToQrText_ThenTryParse_RoundTrips()
    {
        var original = new PairingQrPayload(
            Guid.NewGuid(),
            "Ноутбук Саши", // non-ASCII on purpose — exercises the escaping
            "a1b2c3d4",
            RandomNumberGenerator.GetBytes(32),
            "deadbeef01",
            "192.168.1.23:51843");

        var qrText = original.ToQrText();
        var ok = PairingQrPayload.TryParse(qrText, out var parsed);

        Assert.True(ok);
        Assert.Equal(original.DeviceId, parsed!.DeviceId);
        Assert.Equal(original.DeviceDisplayName, parsed.DeviceDisplayName);
        Assert.Equal(original.Token, parsed.Token);
        Assert.Equal(original.Secret, parsed.Secret); // byte[]: xUnit compares element-wise
        Assert.Equal(original.CertificateFingerprintHex, parsed.CertificateFingerprintHex);
        Assert.Equal(original.HostAndPort, parsed.HostAndPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-winlock-qr")]
    [InlineData("winlock-pair:v2:abc")]
    [InlineData("winlock-pair:v1:not-a-guid:name:token:c2VjcmV0:fp:host:1")]
    public void TryParse_RejectsMalformedInput(string input)
    {
        Assert.False(PairingQrPayload.TryParse(input, out _));
    }
}
