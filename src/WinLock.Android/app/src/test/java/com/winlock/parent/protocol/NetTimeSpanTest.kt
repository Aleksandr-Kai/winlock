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

    @Test
    fun formatHoursMinutes_omitsHours_whenUnderAnHour() {
        assertEquals("45 мин", NetTimeSpan.formatHoursMinutes(45 * 60))
    }

    @Test
    fun formatHoursMinutes_includesHours_whenOverAnHour() {
        assertEquals("1 ч 23 мин", NetTimeSpan.formatHoursMinutes(83 * 60))
    }

    @Test
    fun formatHoursMinutes_zero() {
        assertEquals("0 мин", NetTimeSpan.formatHoursMinutes(0))
    }
}
