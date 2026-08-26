package com.winlock.parent.network

import com.winlock.parent.crypto.ControllerAuthenticator
import com.winlock.parent.model.PairedDevice
import com.winlock.parent.model.ScheduleConfig
import com.winlock.parent.protocol.AuthChallenge
import com.winlock.parent.protocol.AuthResponse
import com.winlock.parent.protocol.AuthResult
import com.winlock.parent.protocol.CommandAck
import com.winlock.parent.protocol.ControllerToServerMessage
import com.winlock.parent.protocol.ExtendTimeCommand
import com.winlock.parent.protocol.LockNowCommand
import com.winlock.parent.protocol.RequestScreenshotCommand
import com.winlock.parent.protocol.ScheduleSnapshot
import com.winlock.parent.protocol.ScreenshotResult
import com.winlock.parent.protocol.ServerToControllerMessage
import com.winlock.parent.protocol.StatusUpdate
import com.winlock.parent.protocol.UnlockNowCommand
import com.winlock.parent.protocol.UpdateScheduleCommand
import com.winlock.parent.protocol.WireJson
import kotlinx.coroutines.CancellableContinuation
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import java.util.Base64
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * One live connection to one paired PC's WebSocket channel: authenticates, then relays
 * status pushes and command round-trips. Not shared across devices — the detail screen for
 * a given PC owns one of these for as long as it's open.
 */
class AgentConnection(private val device: PairedDevice) {
    var onStatus: ((StatusUpdate) -> Unit)? = null
    var onSchedule: ((ScheduleConfig) -> Unit)? = null
    var onDisconnected: (() -> Unit)? = null

    private var client: OkHttpClient? = null
    private var socket: WebSocket? = null
    private val pending = ConcurrentHashMap<String, CancellableContinuation<ServerToControllerMessage>>()

    suspend fun connect() = suspendCancellableCoroutine<Unit> { cont ->
        val okClient = CertificatePinning.buildPinnedClient(device.certificateFingerprintHex)
        client = okClient
        val request = Request.Builder().url("wss://${device.hostAndPort}/agent/ws").build()
        var authSettled = false

        socket = okClient.newWebSocket(
            request,
            object : WebSocketListener() {
                override fun onMessage(webSocket: WebSocket, text: String) {
                    val message = try {
                        WireJson.decodeFromString<ServerToControllerMessage>(text)
                    } catch (e: Exception) {
                        return
                    }

                    when (message) {
                        is AuthChallenge -> {
                            val secret = Base64.getDecoder().decode(device.secretBase64)
                            val responseB64 = ControllerAuthenticator.computeAuthResponse(secret, message.nonce)
                            webSocket.send(
                                WireJson.encodeToString<ControllerToServerMessage>(
                                    AuthResponse(device.controllerId, message.nonce, responseB64),
                                ),
                            )
                        }

                        is AuthResult -> {
                            if (!authSettled) {
                                authSettled = true
                                if (message.success) {
                                    cont.resume(Unit)
                                } else {
                                    cont.resumeWithException(IllegalStateException("Аутентификация отклонена ПК."))
                                }
                            }
                        }

                        is StatusUpdate -> onStatus?.invoke(message)
                        is ScheduleSnapshot -> onSchedule?.invoke(message.schedule)
                        is CommandAck -> pending.remove(message.requestId)?.resume(message)
                        is ScreenshotResult -> pending.remove(message.requestId)?.resume(message)
                    }
                }

                override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                    if (!authSettled) {
                        authSettled = true
                        cont.resumeWithException(t)
                    }
                    failAllPending(t)
                    onDisconnected?.invoke()
                }

                override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                    failAllPending(IllegalStateException("Соединение закрыто."))
                    onDisconnected?.invoke()
                }
            },
        )

        cont.invokeOnCancellation { socket?.cancel() }
    }

    suspend fun extendTime(minutes: Int): Boolean {
        val requestId = newRequestId()
        val result = sendAndWait(requestId, ExtendTimeCommand(requestId, minutes))
        return (result as? CommandAck)?.success == true
    }

    suspend fun updateSchedule(schedule: ScheduleConfig): Boolean {
        val requestId = newRequestId()
        val result = sendAndWait(requestId, UpdateScheduleCommand(requestId, schedule))
        return (result as? CommandAck)?.success == true
    }

    suspend fun requestScreenshot(): ScreenshotResult {
        val requestId = newRequestId()
        return sendAndWait(requestId, RequestScreenshotCommand(requestId)) as ScreenshotResult
    }

    /** Always succeeds. */
    suspend fun lockNow(): Boolean {
        val requestId = newRequestId()
        val result = sendAndWait(requestId, LockNowCommand(requestId))
        return (result as? CommandAck)?.success == true
    }

    /** Can fail — see [CommandAck.errorMessage] on a false result — if the PC's budget has
     * since run out; the caller should surface that message rather than just "didn't work". */
    suspend fun unlockNow(): CommandAck {
        val requestId = newRequestId()
        return sendAndWait(requestId, UnlockNowCommand(requestId)) as CommandAck
    }

    private suspend fun sendAndWait(
        requestId: String,
        message: ControllerToServerMessage,
    ): ServerToControllerMessage =
        suspendCancellableCoroutine { cont ->
            val currentSocket = socket
            if (currentSocket == null) {
                cont.resumeWithException(IllegalStateException("Нет соединения с ПК."))
                return@suspendCancellableCoroutine
            }

            pending[requestId] = cont
            cont.invokeOnCancellation { pending.remove(requestId) }
            currentSocket.send(WireJson.encodeToString(message))
        }

    private fun failAllPending(t: Throwable) {
        pending.keys.toList().forEach { key ->
            pending.remove(key)?.resumeWithException(t)
        }
    }

    private fun newRequestId(): String = UUID.randomUUID().toString().replace("-", "").take(8)

    fun close() {
        socket?.close(1000, "bye")
        client?.dispatcher?.executorService?.shutdown()
    }
}
