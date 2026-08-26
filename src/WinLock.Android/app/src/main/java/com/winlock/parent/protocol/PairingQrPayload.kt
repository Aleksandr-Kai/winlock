package com.winlock.parent.protocol

import java.net.URLDecoder
import java.util.Base64

/**
 * What the PC encodes into the pairing QR code. Mirrors
 * WinLock.Core.Pairing.PairingQrPayload.ToQrText/TryParse exactly:
 * `winlock-pair:v1:{deviceId:N}:{urlEscapedDisplayName}:{token}:{secretBase64}:{fingerprintHex}:{hostAndPort}`
 *
 * The scanned QR is itself the secure channel — nobody but someone looking at this screen
 * can read it — so the shared secret travels inside it directly and is never sent over the
 * network, before or after pairing.
 */
data class PairingQrPayload(
    val deviceId: String,
    val deviceDisplayName: String,
    val token: String,
    val secret: ByteArray,
    val certificateFingerprintHex: String,
    val hostAndPort: String,
) {
    companion object {
        fun tryParse(text: String): PairingQrPayload? {
            // hostAndPort is last and may itself contain a colon, so cap the split rather
            // than splitting on every colon in the string.
            val parts = text.split(":", limit = 8)
            if (parts.size != 8 || parts[0] != "winlock-pair" || parts[1] != "v1") return null

            val deviceId = GuidUtil.fromNFormat(parts[2]) ?: return null

            val secret = try {
                Base64.getDecoder().decode(parts[5])
            } catch (e: IllegalArgumentException) {
                return null
            }

            val displayName = try {
                URLDecoder.decode(parts[3], "UTF-8")
            } catch (e: Exception) {
                return null
            }

            return PairingQrPayload(
                deviceId = deviceId,
                deviceDisplayName = displayName,
                token = parts[4],
                secret = secret,
                certificateFingerprintHex = parts[6],
                hostAndPort = parts[7],
            )
        }
    }
}
