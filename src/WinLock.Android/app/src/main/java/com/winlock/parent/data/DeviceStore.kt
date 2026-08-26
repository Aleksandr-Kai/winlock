package com.winlock.parent.data

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import com.winlock.parent.model.PairedDevice
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/**
 * Persists the list of paired PCs. Backed by [EncryptedSharedPreferences] (Android Keystore
 * under the hood) rather than plain preferences, since each entry carries a secret that
 * grants control over a specific machine.
 */
class DeviceStore(context: Context) {
    private val json = Json { ignoreUnknownKeys = true }

    private val prefs = run {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()

        EncryptedSharedPreferences.create(
            context,
            "winlock_devices",
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
        )
    }

    fun loadAll(): List<PairedDevice> {
        val raw = prefs.getString(KEY, null) ?: return emptyList()
        return try {
            json.decodeFromString<List<PairedDevice>>(raw)
        } catch (e: Exception) {
            // Fail safe to an empty list rather than crash on startup with corrupt prefs.
            emptyList()
        }
    }

    fun add(device: PairedDevice) {
        val current = loadAll().filterNot { it.deviceId == device.deviceId }
        saveAll(current + device)
    }

    fun remove(deviceId: String) {
        saveAll(loadAll().filterNot { it.deviceId == deviceId })
    }

    private fun saveAll(devices: List<PairedDevice>) {
        prefs.edit().putString(KEY, json.encodeToString(devices)).apply()
    }

    companion object {
        private const val KEY = "paired_devices_v1"
    }
}
