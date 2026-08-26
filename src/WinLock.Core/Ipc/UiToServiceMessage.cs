using System.Text.Json.Serialization;

namespace WinLock.Core.Ipc;

/// <summary>Messages the UI helper sends up the named pipe to the service.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RequestChallenge), "requestChallenge")]
[JsonDerivedType(typeof(RedeemOfflineUnlock), "redeem")]
[JsonDerivedType(typeof(BeginPairingRequest), "beginPairing")]
[JsonDerivedType(typeof(CancelPairingRequest), "cancelPairing")]
public abstract record UiToServiceMessage;

/// <summary>Sent when the lock screen's unlock panel is opened, or the user asks for a
/// fresh QR (e.g. the previous one was burned by too many wrong-code attempts).</summary>
public sealed record RequestChallenge : UiToServiceMessage;

public sealed record RedeemOfflineUnlock(long ChallengeId, int Minutes, string Code) : UiToServiceMessage;

/// <summary>Sent only by the separate Setup tool (<c>WinLock.Agent.UI.exe --pair</c>), never
/// by the lock screen. The service additionally verifies the caller is an administrator
/// before honoring this — see <c>NamedPipeServerHost</c>.</summary>
public sealed record BeginPairingRequest : UiToServiceMessage;

public sealed record CancelPairingRequest : UiToServiceMessage;
