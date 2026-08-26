package com.winlock.parent.ui

import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.Checkbox
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.unit.dp
import com.winlock.parent.data.DeviceStore
import com.winlock.parent.model.LockReason
import com.winlock.parent.model.PairedDevice
import com.winlock.parent.model.ScheduleConfig
import com.winlock.parent.model.TimeWindow
import com.winlock.parent.network.AgentConnection
import com.winlock.parent.network.rootCauseMessage
import com.winlock.parent.protocol.LockReasonText
import com.winlock.parent.protocol.NetTimeSpan
import com.winlock.parent.protocol.StatusUpdate
import kotlinx.coroutines.launch

private val Days = listOf(
    "Monday" to "Пн", "Tuesday" to "Вт", "Wednesday" to "Ср", "Thursday" to "Чт",
    "Friday" to "Пт", "Saturday" to "Сб", "Sunday" to "Вс",
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeviceDetailScreen(
    deviceId: String,
    deviceStore: DeviceStore,
    onBack: () -> Unit,
    onOfflineUnlock: () -> Unit,
) {
    val scope = rememberCoroutineScope()
    var device by remember { mutableStateOf<PairedDevice?>(null) }
    var connection by remember { mutableStateOf<AgentConnection?>(null) }
    var connectionLabel by remember { mutableStateOf("Подключение...") }
    var status by remember { mutableStateOf<StatusUpdate?>(null) }
    var statusMessage by remember { mutableStateOf<String?>(null) }
    var statusIsError by remember { mutableStateOf(false) }
    var showForgetDialog by remember { mutableStateOf(false) }
    var screenshotBytes by remember { mutableStateOf<ByteArray?>(null) }

    val dayChecks = remember { mutableStateMapOf(*Days.map { it.first to false }.toTypedArray()) }
    var startTime by remember { mutableStateOf("08:00") }
    var endTime by remember { mutableStateOf("20:00") }
    var dailyLimitText by remember { mutableStateOf("120") }

    DisposableEffect(deviceId) {
        val loaded = deviceStore.loadAll().firstOrNull { it.deviceId == deviceId }
        device = loaded

        var conn: AgentConnection? = null
        if (loaded != null) {
            conn = AgentConnection(loaded)
            connection = conn
            conn.onStatus = { status = it }
            conn.onSchedule = { schedule ->
                Days.forEach { (key, _) -> dayChecks[key] = schedule.allowedWindows[key]?.isNotEmpty() == true }
                val firstWindow = schedule.allowedWindows.values.firstOrNull { it.isNotEmpty() }?.firstOrNull()
                if (firstWindow != null) {
                    startTime = firstWindow.start.take(5) // "08:00:00" -> "08:00"
                    endTime = firstWindow.end.take(5)
                }
                if (schedule.isConfigured) {
                    dailyLimitText = schedule.dailyLimitMinutes.toString()
                }
            }
            conn.onDisconnected = { connectionLabel = "Соединение потеряно" }
            scope.launch {
                try {
                    conn.connect()
                    connectionLabel = "Онлайн"
                } catch (e: Exception) {
                    connectionLabel = "Не удалось подключиться: ${e.rootCauseMessage()}"
                }
            }
        }

        onDispose { conn?.close() }
    }

    if (showForgetDialog && device != null) {
        AlertDialog(
            onDismissRequest = { showForgetDialog = false },
            title = { Text("Отвязать устройство?") },
            text = {
                Text(
                    "«${device!!.displayName}» будет удалено из списка на этом телефоне. " +
                        "На самом ПК привязка тоже должна быть отозвана отдельно.",
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    deviceStore.remove(device!!.deviceId)
                    showForgetDialog = false
                    onBack()
                }) { Text("Отвязать") }
            },
            dismissButton = {
                TextButton(onClick = { showForgetDialog = false }) { Text("Отмена") }
            },
        )
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(device?.displayName ?: "WinLock") },
                actions = {
                    TextButton(onClick = { showForgetDialog = true }) { Text("Отвязать") }
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
                    Text(connectionLabel, style = MaterialTheme.typography.bodySmall)
                    val current = status
                    if (current != null) {
                        Text(
                            if (current.isLocked) "Заблокирован (${LockReasonText.describe(LockReason.fromWireValue(current.reason))})" else "Разблокирован",
                            style = MaterialTheme.typography.headlineSmall,
                            color = if (current.isLocked) Color(0xFFE5484D) else Color(0xFF5CE65C),
                        )
                        val seconds = NetTimeSpan.parseToSeconds(current.remainingBudget)
                        if (seconds != null) {
                            Text("Осталось сегодня: ${NetTimeSpan.formatHms(seconds)}", style = MaterialTheme.typography.bodyMedium)
                        }
                    } else {
                        Text("—", style = MaterialTheme.typography.headlineSmall)
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
                modifier = Modifier.fillMaxWidth().padding(top = 12.dp),
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

            Text("Расписание", style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(top = 24.dp, bottom = 4.dp))
            Text(
                "Разрешённые дни и часы, плюс дневной лимит. Применяется одинаково ко всем выбранным дням.",
                style = MaterialTheme.typography.bodySmall,
            )

            Days.chunked(2).forEach { rowDays ->
                Row(verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) {
                    rowDays.forEach { (key, label) ->
                        Checkbox(checked = dayChecks[key] == true, onCheckedChange = { dayChecks[key] = it })
                        Text(label, modifier = Modifier.padding(end = 16.dp))
                    }
                }
            }

            Row(modifier = Modifier.fillMaxWidth().padding(top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(value = startTime, onValueChange = { startTime = it }, label = { Text("С") }, modifier = Modifier.weight(1f))
                OutlinedTextField(value = endTime, onValueChange = { endTime = it }, label = { Text("До") }, modifier = Modifier.weight(1f))
            }

            OutlinedTextField(
                value = dailyLimitText,
                onValueChange = { dailyLimitText = it },
                label = { Text("Лимит в день (мин)") },
                modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
            )

            Button(
                onClick = {
                    val dailyLimit = dailyLimitText.toIntOrNull()
                    if (dailyLimit == null || dailyLimit <= 0) {
                        statusMessage = "Введите положительное число минут для дневного лимита."
                        statusIsError = true
                        return@Button
                    }

                    val window = TimeWindow(normalizeTime(startTime), normalizeTime(endTime))
                    val allowedWindows = dayChecks.filterValues { it }.keys.associateWith { listOf(window) }
                    val schedule = ScheduleConfig(isConfigured = true, allowedWindows = allowedWindows, dailyLimitMinutes = dailyLimit)

                    scope.launch {
                        val ok = connection?.updateSchedule(schedule) == true
                        statusMessage = if (ok) "Расписание сохранено." else "ПК отклонил расписание."
                        statusIsError = !ok
                    }
                },
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp),
            ) { Text("Сохранить расписание") }

            Text("Прочее", style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(top = 24.dp, bottom = 8.dp))

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
                modifier = Modifier.fillMaxWidth(),
            ) { Text("Запросить снимок экрана") }

            screenshotBytes?.let { bytes ->
                val bitmap = remember(bytes) { BitmapFactory.decodeByteArray(bytes, 0, bytes.size) }
                if (bitmap != null) {
                    Image(
                        bitmap = bitmap.asImageBitmap(),
                        contentDescription = "Снимок экрана",
                        modifier = Modifier.fillMaxWidth().height(200.dp).padding(top = 8.dp),
                    )
                }
            }

            Button(onClick = onOfflineUnlock, modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
                Text("Оффлайн-разблокировка")
            }

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

private fun normalizeTime(text: String): String {
    val trimmed = text.trim()
    val parts = trimmed.split(":")
    if (parts.size < 2) return "00:00:00"
    val hour = parts[0].toIntOrNull()?.coerceIn(0, 23) ?: 0
    val minute = parts[1].toIntOrNull()?.coerceIn(0, 59) ?: 0
    return "%02d:%02d:00".format(hour, minute)
}
