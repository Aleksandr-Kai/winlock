package com.winlock.parent.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/** "HH:mm:ss" — matches .NET's built-in TimeOnly JSON format exactly. */
@Serializable
data class TimeWindow(
    @SerialName("Start") val start: String,
    @SerialName("End") val end: String,
)

/**
 * Mirrors WinLock.Core.Models.ScheduleConfig. Field names carry the exact wire casing via
 * [SerialName] so the Kotlin property names can stay idiomatic camelCase.
 *
 * [allowedWindows] keys are English day names ("Monday".."Sunday") — how System.Text.Json
 * writes a Dictionary<DayOfWeek, ...> key (always the enum's name, never its numeric value,
 * since JSON object keys must be strings).
 */
@Serializable
data class ScheduleConfig(
    @SerialName("IsConfigured") val isConfigured: Boolean = false,
    @SerialName("AllowedWindows") val allowedWindows: Map<String, List<TimeWindow>> = emptyMap(),
    @SerialName("DailyLimitMinutes") val dailyLimitMinutes: Int = 120,
)
