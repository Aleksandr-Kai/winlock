package com.winlock.parent.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.util.Base64

class PairingQrPayloadTest {
    @Test
    fun tryParse_parsesARealQrTextProducedByTheService() {
        // Captured verbatim from a live WinLock.Service instance.
        val qrText = "winlock-pair:v1:4fcca5edbaef45bdb2bfb4b4608b2822:kai-lenovo:" +
            "5efe53de3ea63adc:yK/V8A0nMpgXgtuKiu1Stc78BXbk+PhND354Df5D3V8=:" +
            "ea52c2201e180bd195d70a5b5f454401e7ad92c98d3759cea72e7cdd181b4da1:192.168.3.15:51843"

        val payload = PairingQrPayload.tryParse(qrText)

        requireNotNull(payload)
        assertEquals("4fcca5ed-baef-45bd-b2bf-b4b4608b2822", payload.deviceId)
        assertEquals("kai-lenovo", payload.deviceDisplayName)
        assertEquals("5efe53de3ea63adc", payload.token)
        assertEquals(
            "yK/V8A0nMpgXgtuKiu1Stc78BXbk+PhND354Df5D3V8=",
            Base64.getEncoder().encodeToString(payload.secret),
        )
        assertEquals("ea52c2201e180bd195d70a5b5f454401e7ad92c98d3759cea72e7cdd181b4da1", payload.certificateFingerprintHex)
        assertEquals("192.168.3.15:51843", payload.hostAndPort)
    }

    @Test
    fun tryParse_handlesUrlEscapedNonAsciiDisplayName() {
        val name = "Ноутбук Саши"
        val encoded = java.net.URLEncoder.encode(name, "UTF-8")
        val secretB64 = Base64.getEncoder().encodeToString(ByteArray(32))
        val qrText = "winlock-pair:v1:4fcca5edbaef45bdb2bfb4b4608b2822:$encoded:tok:$secretB64:fp:1.2.3.4:9"

        val payload = PairingQrPayload.tryParse(qrText)

        assertEquals(name, payload?.deviceDisplayName)
    }

    @Test
    fun tryParse_rejectsMalformedInput() {
        assertNull(PairingQrPayload.tryParse(""))
        assertNull(PairingQrPayload.tryParse("not-a-winlock-qr"))
        assertNull(PairingQrPayload.tryParse("winlock-pair:v2:abc"))
        assertNull(PairingQrPayload.tryParse("winlock-pair:v1:not-a-guid:name:token:c2VjcmV0:fp:host:1"))
    }
}
