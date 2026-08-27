package com.winlock.parent.ui

import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.LockOpen
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.unit.dp
import com.winlock.parent.data.DeviceStore
import com.winlock.parent.model.LockReason
import com.winlock.parent.model.PairedDevice
import com.winlock.parent.network.AgentConnection
import com.winlock.parent.network.rootCauseMessage
import com.winlock.parent.protocol.LockReasonText
import com.winlock.parent.protocol.NetTimeSpan
import com.winlock.parent.protocol.StateRecoveryWarning
import com.winlock.parent.protocol.StatusUpdate
import kotlinx.coroutines.launch

private val OnlineColor = Color(0xFF5CE65C)
private val OfflineColor = Color(0xFFE5484D)
private val UnlockedColor = Color(0xFF5CE65C)
private val LockedColor = Color(0xFFE5484D)
private val UnknownColor = Color(0xFF9AA5B1)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeviceDetailScreen(
    deviceId: String,
    deviceStore: DeviceStore,
    onBack: () -> Unit,
    onOpenSettings: () -> Unit,
    onOpenSchedule: () -> Unit,
) {
    val scope = rememberCoroutineScope()
    var device by remember { mutableStateOf<PairedDevice?>(null) }
    var connection by remember { mutableStateOf<AgentConnection?>(null) }
    var isConnected by remember { mutableStateOf(false) }
    var status by remember { mutableStateOf<StatusUpdate?>(null) }
    var statusMessage by remember { mutableStateOf<String?>(null) }
    var statusIsError by remember { mutableStateOf(false) }
    var stateRecoveryWarning by remember { mutableStateOf<StateRecoveryWarning?>(null) }
    var screenshotBytes by remember { mutableStateOf<ByteArray?>(null) }
    var showWheelPicker by remember { mutableStateOf(false) }

    fun startConnection(target: PairedDevice) {
        connection?.close()
        isConnected = false
        status = null

        val conn = AgentConnection(target)
        connection = conn
        conn.onStatus = { status = it }
        conn.onStateRecoveryWarning = { stateRecoveryWarning = it }
        conn.onDisconnected = { isConnected = false }
        scope.launch {
            try {
                conn.connect()
                isConnected = true
            } catch (e: Exception) {
                statusMessage = "Не удалось подключиться: ${e.rootCauseMessage()}"
                statusIsError = true
            }
        }
    }

    DisposableEffect(deviceId) {
        val loaded = deviceStore.loadAll().firstOrNull { it.deviceId == deviceId }
        device = loaded
        if (loaded != null) startConnection(loaded)

        onDispose { connection?.close() }
    }

    stateRecoveryWarning?.let { warning ->
        AlertDialog(
            onDismissRequest = {},
            title = { Text("⚠ Данные на ПК были сброшены") },
            text = {
                Text(
                    "На компьютере не удалось прочитать сохранённые данные, и он начал с чистого " +
                        "листа: расписание, дневной лимит и привязки родителей сброшены (этот телефон " +
                        "остался привязан, раз вы видите это сообщение). Настройте расписание заново.\n\n" +
                        "Когда: ${warning.occurredAtUtc.replace('T', ' ').take(19)} (UTC)\n" +
                        "Причина: ${warning.reason}",
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    scope.launch {
                        connection?.acknowledgeStateRecovery()
                        stateRecoveryWarning = null
                    }
                }) { Text("Понятно") }
            },
        )
    }

    if (showWheelPicker) {
        val currentSeconds = (status?.let { NetTimeSpan.parseToSeconds(it.remainingBudget) } ?: 0).toInt()
        WheelTimePickerDialog(
            initialHour = currentSeconds / 3600,
            initialMinute = (currentSeconds % 3600) / 60,
            onDismiss = { showWheelPicker = false },
            onConfirm = { hourOfDay, minute ->
                showWheelPicker = false
                scope.launch {
                    val ok = connection?.setRemainingTime(hourOfDay * 60 + minute) == true
                    statusMessage = if (ok) "Лимит установлен: %02d:%02d.".format(hourOfDay, minute) else "ПК отклонил запрос."
                    statusIsError = !ok
                }
            },
        )
    }

    screenshotBytes?.let { bytes ->
        AlertDialog(
            onDismissRequest = { screenshotBytes = null },
            title = { Text("Снимок экрана") },
            text = {
                val bitmap = remember(bytes) { BitmapFactory.decodeByteArray(bytes, 0, bytes.size) }
                if (bitmap != null) {
                    Image(
                        bitmap = bitmap.asImageBitmap(),
                        contentDescription = "Снимок экрана",
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            },
            confirmButton = {
                TextButton(onClick = { screenshotBytes = null }) { Text("Закрыть") }
            },
        )
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(device?.displayName ?: "WinLock") },
                actions = {
                    IconButton(onClick = onOpenSettings) {
                        Icon(Icons.Filled.Settings, contentDescription = "Настройки")
                    }
                },
            )
        },
    ) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .padding(20.dp)
                .verticalScroll(rememberScrollState()),
        ) {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        Box(
                            modifier = Modifier
                                .size(10.dp)
                                .background(if (isConnected) OnlineColor else OfflineColor, CircleShape),
                        )

                        val current = status
                        when {
                            !isConnected || current == null -> Icon(
                                Icons.Outlined.Lock,
                                contentDescription = "Состояние неизвестно",
                                tint = UnknownColor,
                                modifier = Modifier.size(22.dp),
                            )
                            current.isLocked -> Icon(
                                Icons.Filled.Lock,
                                contentDescription = "Заблокирован",
                                tint = LockedColor,
                                modifier = Modifier.size(22.dp),
                            )
                            else -> Icon(
                                Icons.Filled.LockOpen,
                                contentDescription = "Разблокирован",
                                tint = UnlockedColor,
                                modifier = Modifier.size(22.dp),
                            )
                        }
                    }

                    val current = status
                    if (current != null) {
                        if (current.isLocked) {
                            Text(
                                LockReasonText.describe(LockReason.fromWireValue(current.reason)),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                modifier = Modifier.padding(top = 6.dp),
                            )
                        }
                        val seconds = NetTimeSpan.parseToSeconds(current.remainingBudget)
                        if (seconds != null) {
                            Text(
                                "Осталось сегодня: ${NetTimeSpan.formatHms(seconds)}",
                                style = MaterialTheme.typography.bodyMedium,
                                modifier = Modifier.padding(top = 6.dp),
                            )
                        }
                    }
                }
            }

            val isManuallyLocked = status?.let { it.isLocked && LockReason.fromWireValue(it.reason) == LockReason.ManuallyLocked } == true
            Button(
                onClick = {
                    scope.launch {
                        if (isManuallyLocked) {
                            val ack = connection?.unlockNow()
                            statusMessage = if (ack?.success == true) "Разблокировано." else (ack?.errorMessage ?: "ПК отклонил запрос.")
                            statusIsError = ack?.success != true
                        } else {
                            val ok = connection?.lockNow() == true
                            statusMessage = if (ok) "Заблокировано." else "ПК отклонил запрос."
                            statusIsError = !ok
                        }
                    }
                },
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp),
            ) { Text(if (isManuallyLocked) "Разблокировать" else "Заблокировать") }

            Text("Продлить время", style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(top = 20.dp, bottom = 8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                listOf(15, 30, 60).forEach { minutes ->
                    Button(
                        onClick = {
                            scope.launch {
                                val ok = connection?.extendTime(minutes) == true
                                statusMessage = if (ok) "Добавлено $minutes мин." else "ПК отклонил запрос."
                                statusIsError = !ok
                            }
                        },
                        modifier = Modifier.weight(1f),
                    ) { Text("+$minutes мин") }
                }
            }

            Button(
                onClick = { showWheelPicker = true },
                modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
            ) { Text("Изменить лимит") }

            Button(
                onClick = onOpenSchedule,
                modifier = Modifier.fillMaxWidth().padding(top = 20.dp),
            ) { Text("Расписание") }

            Button(
                onClick = {
                    scope.launch {
                        statusMessage = "Запрашиваем снимок экрана..."
                        statusIsError = false
                        try {
                            val result = connection?.requestScreenshot()
                            if (result?.success == true && result.imageBase64 != null) {
                                screenshotBytes = java.util.Base64.getDecoder().decode(result.imageBase64)
                                statusMessage = null
                            } else {
                                statusMessage = result?.errorMessage ?: "Не удалось получить снимок."
                                statusIsError = true
                            }
                        } catch (e: Exception) {
                            statusMessage = "Ошибка: ${e.rootCauseMessage()}"
                            statusIsError = true
                        }
                    }
                },
                modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
            ) { Text("Скриншот") }

            statusMessage?.let {
                Text(
                    it,
                    color = if (statusIsError) Color(0xFFE5484D) else Color(0xFF5CE65C),
                    modifier = Modifier.padding(top = 16.dp),
                )
            }
        }
    }
}
