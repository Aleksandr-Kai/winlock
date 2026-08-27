package com.winlock.parent.protocol

/** Simple dot-separated numeric version comparison ("1.2.0" vs "1.10.0") — not full semver
 * (no pre-release/build metadata), which is all AgentVersion/AppVersion ever use here. */
object VersionCompare {
    /** True if [actual] is at least [minimum]. Also true if either string doesn't parse as a
     * plain dotted-number version — refusing to compare shouldn't read as "outdated". */
    fun isAtLeast(actual: String, minimum: String): Boolean {
        val a = parse(actual) ?: return true
        val m = parse(minimum) ?: return true
        for (i in 0 until maxOf(a.size, m.size)) {
            val av = a.getOrElse(i) { 0 }
            val mv = m.getOrElse(i) { 0 }
            if (av != mv) return av > mv
        }
        return true
    }

    private fun parse(version: String): List<Int>? =
        version.trim().split(".").map { it.toIntOrNull() ?: return null }
}
