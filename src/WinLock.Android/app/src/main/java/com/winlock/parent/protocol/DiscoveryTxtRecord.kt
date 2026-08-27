package com.winlock.parent.protocol

/** Reads the fields WinLock's mDNS/DNS-SD advertisement (`_winlock._tcp.local`) puts into its
 * TXT record — pulled out as a pure function, taking a plain attribute map rather than
 * `android.net.nsd.NsdServiceInfo` directly, so it's testable without the Android framework. */
object DiscoveryTxtRecord {
    /** The PC advertises its device ID in the same N-format (32 hex chars, no dashes) the QR
     * pairing payload uses — normalized here through [GuidUtil.fromNFormat] to the dashed form
     * [com.winlock.parent.model.PairedDevice.deviceId] is actually stored in, the same way
     * pairing already does. Skipping that step here meant a discovered device's ID could
     * never equal a paired device's ID even when they were the same device — silently
     * breaking every deviceId comparison that used a discovery result. */
    fun parseDeviceId(attributes: Map<String, ByteArray?>): String? {
        val raw = attributes["deviceId"]?.toString(Charsets.UTF_8)?.trim()?.takeIf { it.isNotEmpty() } ?: return null
        return GuidUtil.fromNFormat(raw)
    }
}
