package com.winlock.parent.network

/** SSL/TLS failures on Android are routinely wrapped (e.g. checkServerTrusted throwing
 * CertificateException surfaces to callers as SSLHandshakeException) — walk to the deepest
 * cause with an actual message so pinning failures show the real reason, not a generic
 * "Handshake failed" from the wrapper. */
fun Throwable.rootCauseMessage(): String {
    var current: Throwable = this
    var deepestWithMessage: String? = current.message
    while (true) {
        val next = current.cause ?: break
        if (next === current) break
        current = next
        current.message?.let { deepestWithMessage = it }
    }
    return deepestWithMessage ?: current.javaClass.simpleName
}
