namespace WinLock.Core.Network;

/// <summary>POST body for completing pairing (<c>POST /agent/pair</c>). The phone already
/// knows the shared secret from reading the QR — this call proves it read the *live* QR by
/// echoing back the one-time token, without transmitting the secret itself.</summary>
public sealed record PairCompletionRequest(string Token, string ControllerDisplayName);

public sealed record PairCompletionResponse(bool Success, Guid? ControllerId, Guid DeviceId, string DeviceDisplayName);

/// <summary>Response for the loopback-only <c>POST /agent/pair/begin</c> — see its mapping
/// in the service's <c>Program.cs</c> for why it exists alongside the pipe-based flow.</summary>
public sealed record BeginPairingResponse(string QrText, DateTimeOffset ExpiresAtUtc);
