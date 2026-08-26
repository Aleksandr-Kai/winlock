package com.winlock.parent.crypto

import java.util.Base64
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/** Mirrors WinLock.Core.Network.ControllerAuthenticator.ComputeAuthResponse exactly:
 * base64(HMAC-SHA256(secret, UTF8(nonce))). The shared secret is never sent over the
 * network — only this response, which the PC verifies by computing the same value itself.
 *
 * Uses java.util.Base64 (not android.util.Base64) deliberately: it's plain JDK, so this
 * class — and the cross-language test vectors that pin it against the real C# output — run
 * as fast JVM unit tests, no emulator required. Both use the same standard, padded alphabet
 * as .NET's Convert.ToBase64String, so this is a wire-compatible choice, not just convenient. */
object ControllerAuthenticator {
    fun computeAuthResponse(secret: ByteArray, nonce: String): String {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(secret, "HmacSHA256"))
        val hash = mac.doFinal(nonce.toByteArray(Charsets.UTF_8))
        return Base64.getEncoder().encodeToString(hash)
    }
}
