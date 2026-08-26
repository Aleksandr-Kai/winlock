package com.winlock.parent.model

/** Mirrors WinLock.Core.Models.LockReason exactly — same declaration order, since the wire
 * format sends this as a plain integer (System.Text.Json's default enum encoding). */
enum class LockReason {
    None,
    OutsideAllowedWindow,
    BudgetExhausted,
    ClockTamperSuspected,
    ManuallyLocked;

    companion object {
        fun fromWireValue(value: Int): LockReason = entries.getOrElse(value) { None }
    }
}
