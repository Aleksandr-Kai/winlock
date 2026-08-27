package com.winlock.parent.protocol

/** Reads the fields WinLock's mDNS/DNS-SD advertisement (`_winlock._tcp.local`) puts into its
 * TXT record — pulled out as a pure function, taking a plain attribute map rather than
 * `android.net.nsd.NsdServiceInfo` directly, so it's testable without the Android framework. */
object DiscoveryTxtRecord {
    fun parseDeviceId(attributes: Map<String, ByteArray?>): String? =
        attributes["deviceId"]?.toString(Charsets.UTF_8)?.trim()?.takeIf { it.isNotEmpty() }
}
