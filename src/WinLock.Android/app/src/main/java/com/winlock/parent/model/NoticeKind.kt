package com.winlock.parent.model

/** Mirrors WinLock.Core.Models.NoticeKind exactly — same declaration order, since the wire
 * format sends this as a plain integer (System.Text.Json's default enum encoding). */
enum class NoticeKind {
    StateRecovery,
    ServiceStopped;

    companion object {
        fun fromWireValue(value: Int): NoticeKind = entries.getOrElse(value) { StateRecovery }
    }
}
