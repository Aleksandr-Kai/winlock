package com.winlock.parent.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class DiscoveryTxtRecordTest {
    @Test
    fun parseDeviceId_readsThePresentValue_normalizedToDashedForm() {
        // The TXT record carries N-format (no dashes) — the same as the QR pairing payload —
        // but PairedDevice.deviceId is stored dashed, so this must match that or every
        // discovered-vs-paired deviceId comparison in the app silently never matches.
        val attrs = mapOf(
            "deviceId" to "cc1e300455e645829bc5bd4461513505".toByteArray(Charsets.UTF_8),
            "displayName" to "kai-lenovo".toByteArray(Charsets.UTF_8),
        )
        assertEquals("cc1e3004-55e6-4582-9bc5-bd4461513505", DiscoveryTxtRecord.parseDeviceId(attrs))
    }

    @Test
    fun parseDeviceId_returnsNull_whenKeyIsMissing() {
        val attrs = mapOf("displayName" to "kai-lenovo".toByteArray(Charsets.UTF_8))
        assertNull(DiscoveryTxtRecord.parseDeviceId(attrs))
    }

    @Test
    fun parseDeviceId_returnsNull_whenValueIsBlank() {
        val attrs = mapOf("deviceId" to "   ".toByteArray(Charsets.UTF_8))
        assertNull(DiscoveryTxtRecord.parseDeviceId(attrs))
    }

    @Test
    fun parseDeviceId_returnsNull_whenValueIsNull() {
        val attrs = mapOf<String, ByteArray?>("deviceId" to null)
        assertNull(DiscoveryTxtRecord.parseDeviceId(attrs))
    }
}
