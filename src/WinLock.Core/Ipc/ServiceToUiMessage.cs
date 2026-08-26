using System.Text.Json.Serialization;
using WinLock.Core.Models;

namespace WinLock.Core.Ipc;

/// <summary>Messages the service sends down the named pipe to the UI helper process.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(LockCommand), "lock")]
[JsonDerivedType(typeof(UnlockCommand), "unlock")]
[JsonDerivedType(typeof(ChallengeIssued), "challengeIssued")]
[JsonDerivedType(typeof(RedeemResult), "redeemResult")]
[JsonDerivedType(typeof(PairingQrIssued), "pairingQrIssued")]
[JsonDerivedType(typeof(PairingCompleted), "pairingCompleted")]
[JsonDerivedType(typeof(PairingFailed), "pairingFailed")]
public abstract record ServiceToUiMessage;

public sealed record LockCommand(LockReason Reason) : ServiceToUiMessage;

public sealed record UnlockCommand : ServiceToUiMessage;

/// <summary>A freshly issued offline-unlock challenge, ready to render as a QR code.</summary>
public sealed record ChallengeIssued(long ChallengeId, string QrText) : ServiceToUiMessage;

public sealed record RedeemResult(bool Success, string? ErrorMessage) : ServiceToUiMessage;

/// <summary>A freshly issued pairing challenge, ready to render as a QR code, for the Setup tool.</summary>
public sealed record PairingQrIssued(string QrText, DateTimeOffset ExpiresAtUtc) : ServiceToUiMessage;

public sealed record PairingCompleted(string ControllerDisplayName) : ServiceToUiMessage;

public sealed record PairingFailed(string Reason) : ServiceToUiMessage;
