using System.Text.Json.Serialization;
using WinLock.Core.Models;

namespace WinLock.Core.Network;

/// <summary>Messages the PC pushes to a connected, authenticated controller (phone) over the WebSocket.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AuthChallenge), "authChallenge")]
[JsonDerivedType(typeof(AuthResult), "authResult")]
[JsonDerivedType(typeof(StatusUpdate), "status")]
[JsonDerivedType(typeof(CommandAck), "ack")]
[JsonDerivedType(typeof(ScreenshotResult), "screenshotResult")]
[JsonDerivedType(typeof(ScheduleSnapshot), "scheduleSnapshot")]
public abstract record ServerToControllerMessage;

/// <summary>Sent immediately on connect, before anything else is accepted.</summary>
public sealed record AuthChallenge(string Nonce) : ServerToControllerMessage;

public sealed record AuthResult(bool Success) : ServerToControllerMessage;

public sealed record StatusUpdate(
    Guid DeviceId,
    string DeviceDisplayName,
    bool IsLocked,
    LockReason Reason,
    TimeSpan RemainingBudget) : ServerToControllerMessage;

/// <summary>Acknowledges a command that isn't itself answered by a more specific message
/// (e.g. extend-time, schedule update) — correlated back by <see cref="RequestId"/>.</summary>
public sealed record CommandAck(string RequestId, bool Success, string? ErrorMessage) : ServerToControllerMessage;

/// <summary>
/// A single on-demand screenshot, taken only because this specific request asked for one —
/// the agent never captures or streams the screen on its own. JPEG, base64-encoded; fine for
/// an occasional single frame, not meant for anything resembling continuous streaming.
/// </summary>
public sealed record ScreenshotResult(
    string RequestId,
    bool Success,
    string? ErrorMessage,
    string? ImageBase64,
    DateTimeOffset? CapturedAtUtc) : ServerToControllerMessage;

/// <summary>The schedule currently in effect on the PC. Sent right after a controller
/// authenticates, and rebroadcast to every connected controller whenever any one of them
/// changes it — so a second parent's app doesn't keep showing a stale schedule, and a
/// freshly opened app doesn't show empty/default fields for a device that already has one.</summary>
public sealed record ScheduleSnapshot(ScheduleConfig Schedule) : ServerToControllerMessage;
