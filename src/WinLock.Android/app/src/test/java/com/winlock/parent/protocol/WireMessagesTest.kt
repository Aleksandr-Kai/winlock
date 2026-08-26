package com.winlock.parent.protocol

import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** Every "raw" string here was captured verbatim from a real WinLock.Service instance over
 * an actual TLS WebSocket connection — not hand-written — so these tests catch a wire-format
 * mismatch, not just a bug in whatever this Kotlin code assumes the format to be. */
class WireMessagesTest {
    @Test
    fun decodesRealAuthChallenge() {
        val raw = """{"${'$'}type":"authChallenge","Nonce":"08U3H+YApC1RlvPQsKiiOBusO8tLY9EO"}"""

        val message = WireJson.decodeFromString<ServerToControllerMessage>(raw)

        val challenge = message as AuthChallenge
        assertEquals("08U3H+YApC1RlvPQsKiiOBusO8tLY9EO", challenge.nonce)
    }

    @Test
    fun decodesRealStatusUpdate_outsideAllowedWindow() {
        val raw = """{"${'$'}type":"status","DeviceId":"4fcca5ed-baef-45bd-b2bf-b4b4608b2822",""" +
            """"DeviceDisplayName":"kai-lenovo","IsLocked":true,"Reason":1,"RemainingBudget":"02:03:21.3900000"}"""

        val status = WireJson.decodeFromString<ServerToControllerMessage>(raw) as StatusUpdate

        assertTrue(status.isLocked)
        assertEquals(1, status.reason)
        assertEquals(7401L, NetTimeSpan.parseToSeconds(status.remainingBudget)) // 02:03:21 = 7401s
    }

    @Test
    fun decodesRealScreenshotFailureResult() {
        val raw = """{"${'$'}type":"screenshotResult","RequestId":"shot1","Success":false,""" +
            """"ErrorMessage":"only on Windows","ImageBase64":null,"CapturedAtUtc":null}"""

        val result = WireJson.decodeFromString<ServerToControllerMessage>(raw) as ScreenshotResult

        assertEquals(false, result.success)
        assertEquals("only on Windows", result.errorMessage)
    }

    @Test
    fun encodesExtendTimeCommand_withExpectedDiscriminatorAndCasing() {
        val json = WireJson.encodeToString<ControllerToServerMessage>(ExtendTimeCommand("req1", 30))

        assertTrue(json.contains("\"\$type\":\"extendTime\""))
        assertTrue(json.contains("\"RequestId\":\"req1\""))
        assertTrue(json.contains("\"Minutes\":30"))
    }

    @Test
    fun decodesRealPairCompletionResponse_httpCamelCase() {
        val raw = """{"success":true,"controllerId":"9192220c-67cf-4acf-af36-df6354bf5c93",""" +
            """"deviceId":"4fcca5ed-baef-45bd-b2bf-b4b4608b2822","deviceDisplayName":"kai-lenovo"}"""

        val response = HttpJson.decodeFromString<PairCompletionResponse>(raw)

        assertEquals(true, response.success)
        assertEquals("9192220c-67cf-4acf-af36-df6354bf5c93", response.controllerId)
    }
}
