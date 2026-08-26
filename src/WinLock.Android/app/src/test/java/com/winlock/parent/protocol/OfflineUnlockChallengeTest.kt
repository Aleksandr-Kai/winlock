package com.winlock.parent.protocol

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class OfflineUnlockChallengeTest {
    @Test
    fun tryParse_parsesARealChallengeText() {
        val text = "winlock:v1:4fcca5edbaef45bdb2bfb4b4608b2822:42:deadbeef"

        val challenge = OfflineUnlockChallenge.tryParse(text)

        requireNotNull(challenge)
        assertEquals(42L, challenge.challengeId)
        assertEquals("4fcca5ed-baef-45bd-b2bf-b4b4608b2822", challenge.deviceId)
        assertEquals("deadbeef", challenge.integrityTag)
    }

    @Test
    fun tryParse_rejectsMalformedInput() {
        assertNull(OfflineUnlockChallenge.tryParse(""))
        assertNull(OfflineUnlockChallenge.tryParse("not-a-winlock-qr"))
        assertNull(OfflineUnlockChallenge.tryParse("winlock:v2:abc:1:tag"))
        assertNull(OfflineUnlockChallenge.tryParse("winlock:v1:not-a-guid:1:tag"))
        assertNull(OfflineUnlockChallenge.tryParse("winlock:v1:4fcca5edbaef45bdb2bfb4b4608b2822:not-a-number:tag"))
    }
}
