using System.Text.Json.Serialization;
using WinLock.Core.Models;

namespace WinLock.Core.Network;

/// <summary>Messages a connected controller (phone) sends to the PC over the WebSocket.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AuthResponse), "authResponse")]
[JsonDerivedType(typeof(ExtendTimeCommand), "extendTime")]
[JsonDerivedType(typeof(SetRemainingTimeCommand), "setRemainingTime")]
[JsonDerivedType(typeof(UpdateScheduleCommand), "updateSchedule")]
[JsonDerivedType(typeof(RequestScreenshotCommand), "requestScreenshot")]
[JsonDerivedType(typeof(LockNowCommand), "lockNow")]
[JsonDerivedType(typeof(UnlockNowCommand), "unlockNow")]
[JsonDerivedType(typeof(AcknowledgeStateRecoveryCommand), "acknowledgeStateRecovery")]
[JsonDerivedType(typeof(AcknowledgeServiceStoppedCommand), "acknowledgeServiceStopped")]
public abstract record ControllerToServerMessage;

/// <summary>Reply to <see cref="AuthChallenge"/>: proves possession of this controller's
/// secret for this specific nonce, without ever transmitting the secret itself.</summary>
public sealed record AuthResponse(Guid ControllerId, string Nonce, string ResponseBase64) : ControllerToServerMessage;

public sealed record ExtendTimeCommand(string RequestId, int Minutes) : ControllerToServerMessage;

/// <summary>Sets today's remaining budget to an exact value, instead of adding to whatever
/// is currently left.</summary>
public sealed record SetRemainingTimeCommand(string RequestId, int Minutes) : ControllerToServerMessage;

public sealed record UpdateScheduleCommand(string RequestId, ScheduleConfig Schedule) : ControllerToServerMessage;

/// <summary>Explicit, single request for one current screenshot — never a subscription.</summary>
public sealed record RequestScreenshotCommand(string RequestId) : ControllerToServerMessage;

/// <summary>Locks the machine right now, regardless of remaining budget or schedule window.
/// Always succeeds.</summary>
public sealed record LockNowCommand(string RequestId) : ControllerToServerMessage;

/// <summary>Lifts a manual lock. Fails (see the ack's ErrorMessage) if the budget has since
/// run out — the schedule would just re-lock it immediately otherwise.</summary>
public sealed record UnlockNowCommand(string RequestId) : ControllerToServerMessage;

/// <summary>Clears a pending StateRecoveryWarning once a parent has seen it — the only thing
/// that clears it, since it's meant to survive until someone actually notices.</summary>
public sealed record AcknowledgeStateRecoveryCommand(string RequestId) : ControllerToServerMessage;

/// <summary>Clears a pending ServiceStoppedWarning once a parent has seen it — the only thing
/// that clears it, since it's meant to survive until someone actually notices.</summary>
public sealed record AcknowledgeServiceStoppedCommand(string RequestId) : ControllerToServerMessage;
