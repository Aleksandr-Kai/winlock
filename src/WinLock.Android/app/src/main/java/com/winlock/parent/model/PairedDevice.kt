package com.winlock.parent.model

import kotlinx.serialization.Serializable

/** A child's PC this phone has paired with. Several can exist — one parent phone can
 * supervise several machines, each independently paired with its own secret. */
@Serializable
data class PairedDevice(
    val deviceId: String,
    val displayName: String,
    val controllerId: String,
    val secretBase64: String,
    val hostAndPort: String,
    val certificateFingerprintHex: String,
)
