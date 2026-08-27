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
    val message = deepestWithMessage ?: current.javaClass.simpleName

    // Our own exceptions (a rejected cert pin, a rejected auth) are already written in plain
    // Russian for a parent to read directly. Anything else here is a raw OS/library
    // exception — a socket timeout, a DNS failure — full of IPs, ports, and English that
    // isn't meant for an end user; collapse those to one plain explanation instead of
    // showing "failed to connect to /192.168.3.33 (port 51843) from ... after 10000ms".
    return if (message.any { it in 'Ѐ'..'ӿ' }) message else "ПК не отвечает."
}
