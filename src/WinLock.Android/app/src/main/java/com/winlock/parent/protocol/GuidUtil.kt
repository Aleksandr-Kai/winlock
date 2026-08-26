package com.winlock.parent.protocol

/** .NET's Guid.ToString("N") is 32 lowercase hex chars, no dashes — used in the QR text.
 * The HTTP JSON responses instead carry the standard dashed form. Normalize both to the
 * same dashed, lowercase representation so they can be compared and stored consistently. */
internal object GuidUtil {
    private val hexPattern = Regex("^[0-9a-fA-F]{32}$")

    fun fromNFormat(hex: String): String? {
        if (!hexPattern.matches(hex)) return null
        val lower = hex.lowercase()
        return "${lower.substring(0, 8)}-${lower.substring(8, 12)}-${lower.substring(12, 16)}-" +
            "${lower.substring(16, 20)}-${lower.substring(20, 32)}"
    }
}
