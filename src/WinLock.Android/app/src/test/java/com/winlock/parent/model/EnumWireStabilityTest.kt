package com.winlock.parent.model

import org.junit.Assert.assertEquals
import org.junit.Test

/** See WinLock.Core.Tests/EnumWireStabilityTests.cs — both sides hard-code the expected name
 * list for their hand-mirrored enum, so a divergence between this file and the C# source of
 * truth fails a test immediately instead of only showing up at runtime on a real paired PC. */
class EnumWireStabilityTest {
    @Test
    fun lockReason_matchesTheCSharpMirror() {
        assertEquals(
            listOf("None", "OutsideAllowedWindow", "BudgetExhausted", "ClockTamperSuspected", "ManuallyLocked"),
            LockReason.entries.map { it.name },
        )
    }

    @Test
    fun noticeKind_matchesTheCSharpMirror() {
        assertEquals(listOf("StateRecovery", "ServiceStopped"), NoticeKind.entries.map { it.name })
    }
}
