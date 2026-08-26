package com.winlock.parent.network

import com.winlock.parent.protocol.HttpJson
import com.winlock.parent.protocol.PairCompletionRequest
import com.winlock.parent.protocol.PairCompletionResponse
import com.winlock.parent.protocol.PairingQrPayload
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.IOException

/** Completes pairing with a PC after its QR has been read (scanned or pasted). */
object PairingClient {
    private val jsonMediaType = "application/json".toMediaType()

    suspend fun completePairing(qr: PairingQrPayload, controllerDisplayName: String): PairCompletionResponse =
        withContext(Dispatchers.IO) {
            val client = CertificatePinning.buildPinnedClient(qr.certificateFingerprintHex)
            val body = HttpJson.encodeToString(PairCompletionRequest(qr.token, controllerDisplayName))
                .toRequestBody(jsonMediaType)
            val request = Request.Builder()
                .url("https://${qr.hostAndPort}/agent/pair")
                .post(body)
                .build()

            client.newCall(request).execute().use { response ->
                val text = response.body?.string()
                    ?: throw IOException("Пустой ответ от ПК.")
                HttpJson.decodeFromString(text)
            }
        }
}
