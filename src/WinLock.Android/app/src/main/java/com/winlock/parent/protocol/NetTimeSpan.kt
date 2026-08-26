package com.winlock.parent.protocol

/** Parses .NET's TimeSpan JSON format — `[-][d.]hh:mm:ss[.fffffff]` — into total seconds.
 * Only the pieces actually needed here (StatusUpdate.RemainingBudget, capped at a day) are
 * handled, but the day prefix and fractional seconds are parsed defensively anyway. */
internal object NetTimeSpan {
    fun parseToSeconds(text: String): Long? {
        var s = text.trim()
        var negative = false
        if (s.startsWith("-")) {
            negative = true
            s = s.substring(1)
        }

        var days = 0L
        val dotIndex = s.indexOf('.')
        val colonIndex = s.indexOf(':')
        if (dotIndex in 0..<colonIndex.let { if (it < 0) Int.MAX_VALUE else it }) {
            days = s.substring(0, dotIndex).toLongOrNull() ?: return null
            s = s.substring(dotIndex + 1)
        }

        val secondsPart = s.substringBefore('.')
        val pieces = secondsPart.split(":")
        if (pieces.size != 3) return null
        val hours = pieces[0].toLongOrNull() ?: return null
        val minutes = pieces[1].toLongOrNull() ?: return null
        val seconds = pieces[2].toLongOrNull() ?: return null

        val total = days * 86400 + hours * 3600 + minutes * 60 + seconds
        return if (negative) -total else total
    }

    fun formatHms(totalSeconds: Long): String {
        val clamped = totalSeconds.coerceAtLeast(0)
        val h = clamped / 3600
        val m = (clamped % 3600) / 60
        val s = clamped % 60
        return "%02d:%02d:%02d".format(h, m, s)
    }
}
