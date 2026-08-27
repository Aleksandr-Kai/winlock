package com.winlock.parent.protocol

import com.winlock.parent.model.ScheduleConfig
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/**
 * Wire format, confirmed by capturing real traffic against the actual WinLock.Service (not
 * guessed from the C# source): WebSocket messages are PascalCase with a "$type" discriminator
 * (System.Text.Json's default — the service never configures camelCase for these), while the
 * plain HTTP endpoints (/agent/pair, /agent/pair/begin) are camelCase (ASP.NET Core's web
 * defaults). Two separate Json instances below, one per convention.
 */
val WireJson = Json {
    classDiscriminator = "\$type"
    ignoreUnknownKeys = true
}

val HttpJson = Json {
    ignoreUnknownKeys = true
}

@Serializable
sealed class ServerToControllerMessage

@Serializable
@SerialName("authChallenge")
data class AuthChallenge(
    @SerialName("Nonce") val nonce: String,
) : ServerToControllerMessage()

@Serializable
@SerialName("authResult")
data class AuthResult(
    @SerialName("Success") val success: Boolean,
) : ServerToControllerMessage()

@Serializable
@SerialName("status")
data class StatusUpdate(
    @SerialName("DeviceId") val deviceId: String,
    @SerialName("DeviceDisplayName") val deviceDisplayName: String,
    @SerialName("IsLocked") val isLocked: Boolean,
    @SerialName("Reason") val reason: Int,
    @SerialName("RemainingBudget") val remainingBudget: String,
) : ServerToControllerMessage()

@Serializable
@SerialName("ack")
data class CommandAck(
    @SerialName("RequestId") val requestId: String,
    @SerialName("Success") val success: Boolean,
    @SerialName("ErrorMessage") val errorMessage: String? = null,
) : ServerToControllerMessage()

@Serializable
@SerialName("screenshotResult")
data class ScreenshotResult(
    @SerialName("RequestId") val requestId: String,
    @SerialName("Success") val success: Boolean,
    @SerialName("ErrorMessage") val errorMessage: String? = null,
    @SerialName("ImageBase64") val imageBase64: String? = null,
    @SerialName("CapturedAtUtc") val capturedAtUtc: String? = null,
) : ServerToControllerMessage()

/** The schedule currently in effect on the PC — sent right after connecting, and again
 * whenever any connected parent's app changes it, so this app's editor never shows a stale
 * or default schedule for a device that already has one configured. */
@Serializable
@SerialName("scheduleSnapshot")
data class ScheduleSnapshot(
    @SerialName("Schedule") val schedule: ScheduleConfig,
) : ServerToControllerMessage()

/** The PC had to fall back to a fresh, empty state because the previously persisted one was
 * unreadable — schedule, pairings, and certificate were all reset at once. Sent on connect
 * until acknowledged with [AcknowledgeStateRecoveryCommand], since no phone may have been
 * connected at the moment it actually happened. */
@Serializable
@SerialName("stateRecoveryWarning")
data class StateRecoveryWarning(
    @SerialName("OccurredAtUtc") val occurredAtUtc: String,
    @SerialName("Reason") val reason: String,
) : ServerToControllerMessage()

/** The PC agent's version — sent once right after authenticating, the only way this app can
 * tell whether a given PC needs updating without walking up to it. */
@Serializable
@SerialName("agentVersion")
data class AgentVersionInfo(
    @SerialName("Version") val version: String,
) : ServerToControllerMessage()

@Serializable
sealed class ControllerToServerMessage

@Serializable
@SerialName("authResponse")
data class AuthResponse(
    @SerialName("ControllerId") val controllerId: String,
    @SerialName("Nonce") val nonce: String,
    @SerialName("ResponseBase64") val responseBase64: String,
) : ControllerToServerMessage()

@Serializable
@SerialName("extendTime")
data class ExtendTimeCommand(
    @SerialName("RequestId") val requestId: String,
    @SerialName("Minutes") val minutes: Int,
) : ControllerToServerMessage()

/** Sets today's remaining budget to an exact value, instead of adding to whatever is
 * currently left. */
@Serializable
@SerialName("setRemainingTime")
data class SetRemainingTimeCommand(
    @SerialName("RequestId") val requestId: String,
    @SerialName("Minutes") val minutes: Int,
) : ControllerToServerMessage()

@Serializable
@SerialName("updateSchedule")
data class UpdateScheduleCommand(
    @SerialName("RequestId") val requestId: String,
    @SerialName("Schedule") val schedule: ScheduleConfig,
) : ControllerToServerMessage()

@Serializable
@SerialName("requestScreenshot")
data class RequestScreenshotCommand(
    @SerialName("RequestId") val requestId: String,
) : ControllerToServerMessage()

/** Locks the machine right now, regardless of remaining budget or schedule window. Always succeeds. */
@Serializable
@SerialName("lockNow")
data class LockNowCommand(
    @SerialName("RequestId") val requestId: String,
) : ControllerToServerMessage()

/** Lifts a manual lock. Fails (see the ack's error message) if the budget has since run out. */
@Serializable
@SerialName("unlockNow")
data class UnlockNowCommand(
    @SerialName("RequestId") val requestId: String,
) : ControllerToServerMessage()

/** Clears a pending [StateRecoveryWarning] once a parent has seen it. */
@Serializable
@SerialName("acknowledgeStateRecovery")
data class AcknowledgeStateRecoveryCommand(
    @SerialName("RequestId") val requestId: String,
) : ControllerToServerMessage()

@Serializable
data class BeginPairingResponse(val qrText: String, val expiresAtUtc: String)

@Serializable
data class PairCompletionRequest(val token: String, val controllerDisplayName: String)

@Serializable
data class PairCompletionResponse(
    val success: Boolean,
    val controllerId: String? = null,
    val deviceId: String,
    val deviceDisplayName: String,
)
