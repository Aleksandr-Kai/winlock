package com.winlock.parent.protocol

/**
 * Mirrors WinLock.Core.Offline.OfflineUnlockChallenge.ToQrText/TryParse:
 * `winlock:v1:{deviceId:N}:{challengeId}:{integrityTag}`
 *
 * The app doesn't verify [integrityTag] itself — it has no way to, without the device's
 * integrity secret. It only needs [challengeId] to compute a response code, and [deviceId]
 * to pick which paired device's secret to use.
 */
data class OfflineUnlockChallenge(
    val challengeId: Long,
    val deviceId: String,
    val integrityTag: String,
) {
    companion object {
        fun tryParse(text: String): OfflineUnlockChallenge? {
            val parts = text.split(":")
            if (parts.size != 5 || parts[0] != "winlock" || parts[1] != "v1") return null

            val deviceId = GuidUtil.fromNFormat(parts[2]) ?: return null
            val challengeId = parts[3].toLongOrNull() ?: return null

            return OfflineUnlockChallenge(challengeId, deviceId, parts[4])
        }
    }
}
