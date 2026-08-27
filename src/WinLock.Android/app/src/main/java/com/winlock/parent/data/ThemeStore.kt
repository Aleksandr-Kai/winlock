package com.winlock.parent.data

import android.content.Context

enum class ThemeMode { SYSTEM, LIGHT, DARK }

/** Plain (unencrypted) SharedPreferences — unlike DeviceStore, nothing stored here is
 * sensitive, it's just a display preference. */
class ThemeStore(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences("winlock_settings", Context.MODE_PRIVATE)

    fun load(): ThemeMode = when (prefs.getString(KEY, null)) {
        "light" -> ThemeMode.LIGHT
        "dark" -> ThemeMode.DARK
        else -> ThemeMode.SYSTEM
    }

    fun save(mode: ThemeMode) {
        prefs.edit().putString(KEY, mode.name.lowercase()).apply()
    }

    companion object {
        private const val KEY = "theme_mode"
    }
}
