package com.winlock.parent.protocol

import com.winlock.parent.model.LockReason

/** Human-readable Russian text for StatusUpdate.Reason. */
object LockReasonText {
    fun describe(reason: LockReason): String = when (reason) {
        LockReason.None -> "не заблокирован"
        LockReason.OutsideAllowedWindow -> "вне расписания"
        LockReason.BudgetExhausted -> "лимит времени исчерпан"
        LockReason.ClockTamperSuspected -> "подозрение на смену времени на ПК"
        LockReason.ManuallyLocked -> "заблокирован вручную"
    }
}
