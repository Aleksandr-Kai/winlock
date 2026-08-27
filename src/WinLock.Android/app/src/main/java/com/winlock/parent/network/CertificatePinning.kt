package com.winlock.parent.network

import okhttp3.OkHttpClient
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.HostnameVerifier
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

/**
 * Every paired PC's certificate is self-signed on purpose — there's no CA, and none is
 * needed: instead of chain validation, the exact certificate is pinned by its SHA-256
 * fingerprint, which travelled the same secure, out-of-band channel (a scanned QR) as the
 * shared secret itself. A network attacker who doesn't control the screen the QR was read
 * from can't substitute their own certificate even once, let alone silently.
 *
 * The pinning check lives inside the [X509TrustManager] itself (checkServerTrusted), not in
 * a post-hoc interceptor reading back `Response.handshake.peerCertificates` — that list can
 * come back empty rather than throwing, in ways that depend on the JSSE provider/cipher
 * suite, which made an earlier version of this check silently misfire as "no certificate
 * received" even when one plainly was. Doing the real check inside checkServerTrusted is the
 * standard, documented place for exactly this kind of pinning and doesn't have that gap.
 */
object CertificatePinning {
    fun computeFingerprintHex(cert: X509Certificate): String {
        val digest = MessageDigest.getInstance("SHA-256").digest(cert.encoded)
        return digest.joinToString("") { "%02x".format(it) }
    }

    private fun pinningTrustManager(expectedFingerprintHex: String): X509TrustManager =
        object : X509TrustManager {
            override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {}

            override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
                val cert = chain?.firstOrNull()
                    ?: throw CertificateException("Сертификат ПК не получен.")

                val actual = computeFingerprintHex(cert)
                if (!actual.equals(expectedFingerprintHex, ignoreCase = true)) {
                    throw CertificateException(
                        "Сертификат ПК не совпадает с ожидаемым — возможна подмена в сети.",
                    )
                }
                // Fingerprint matches: trust it outright, chain/CA validity is irrelevant here.
            }

            override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
        }

    fun buildPinnedClient(expectedFingerprintHex: String): OkHttpClient {
        val trustManager = pinningTrustManager(expectedFingerprintHex)
        val sslContext = SSLContext.getInstance("TLS")
        sslContext.init(null, arrayOf(trustManager), SecureRandom())

        return OkHttpClient.Builder()
            .sslSocketFactory(sslContext.socketFactory, trustManager)
            .hostnameVerifier(HostnameVerifier { _, _ -> true })
            // Without this, a WebSocket to a PC that goes to sleep (not closed, not
            // rejecting — just silently unresponsive) can sit there looking "connected" for
            // a very long time: nothing ever arrives to trigger a read timeout, and the
            // underlying TCP stack's own dead-peer detection defaults to roughly two hours.
            // A ping this often means a sleeping/unreachable PC gets noticed — and the
            // device-list status dot turns red — within a few tens of seconds instead.
            .pingInterval(10, TimeUnit.SECONDS)
            .build()
    }
}
