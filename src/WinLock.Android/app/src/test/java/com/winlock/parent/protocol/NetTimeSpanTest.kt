package com.winlock.parent.protocol

import org.junit.Assert.assertEquals
import org.junit.Test

class NetTimeSpanTest {
    @Test
    fun parsesWithFractionalSeconds() {
        assertEquals(7134L, NetTimeSpan.parseToSeconds("01:58:54.2960000"))
    }

    @Test
    fun parsesWithoutFractionalSeconds() {
        assertEquals(3661L, NetTimeSpan.parseToSeconds("01:01:01"))
    }

    @Test
    fun parsesZero() {
        assertEquals(0L, NetTimeSpan.parseToSeconds("00:00:00"))
    }

    @Test
    fun formatHms_padsToTwoDigits() {
        assertEquals("01:02:03", NetTimeSpan.formatHms(3723))
    }

    @Test
    fun formatHms_clampsNegativeToZero() {
        assertEquals("00:00:00", NetTimeSpan.formatHms(-5))
    }
}
