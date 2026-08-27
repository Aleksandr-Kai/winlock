package com.winlock.parent.protocol

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class VersionCompareTest {
    @Test
    fun isAtLeast_true_whenEqual() = assertTrue(VersionCompare.isAtLeast("1.0.0", "1.0.0"))

    @Test
    fun isAtLeast_true_whenNewer() = assertTrue(VersionCompare.isAtLeast("1.2.0", "1.0.0"))

    @Test
    fun isAtLeast_false_whenOlder() = assertFalse(VersionCompare.isAtLeast("1.0.0", "1.2.0"))

    @Test
    fun isAtLeast_comparesNumerically_notLexicographically() =
        assertTrue(VersionCompare.isAtLeast("1.10.0", "1.9.0"))

    @Test
    fun isAtLeast_handlesDifferingSegmentCounts() {
        assertTrue(VersionCompare.isAtLeast("1.2.1", "1.2"))
        assertFalse(VersionCompare.isAtLeast("1.2", "1.2.1"))
    }

    @Test
    fun isAtLeast_assumesCompatible_whenUnparseable() {
        assertTrue(VersionCompare.isAtLeast("dev-build", "1.0.0"))
        assertTrue(VersionCompare.isAtLeast("1.0.0", "unknown"))
    }
}
