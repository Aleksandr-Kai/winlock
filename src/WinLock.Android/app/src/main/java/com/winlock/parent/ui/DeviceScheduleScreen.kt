package com.winlock.parent.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.winlock.parent.data.DeviceStore
import com.winlock.parent.model.PairedDevice
import com.winlock.parent.model.ScheduleConfig
import com.winlock.parent.model.TimeWindow
import com.winlock.parent.network.AgentConnection
import kotlinx.coroutines.launch

private val Days = listOf(
    "Monday" to "Пн", "Tuesday" to "Вт", "Wednesday" to "Ср", "Thursday" to "Чт",
    "Friday" to "Пт", "Saturday" to "Сб", "Sunday" to "Вс",
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeviceScheduleScreen(
    deviceId: String,
    deviceStore: DeviceStore,
    onBack: () -> Unit,
) {
    val scope = rememberCoroutineScope()
    var connection by remember { mutableStateOf<AgentConnection?>(null) }
    var statusMessage by remember { mutableStateOf<String?>(null) }
    var statusIsError by remember { mutableStateOf(false) }

    val dayChecks = remember { mutableStateMapOf(*Days.map { it.first to false }.toTypedArray()) }
    var startTime by remember { mutableStateOf("08:00") }
    var endTime by remember { mutableStateOf("20:00") }
    var dailyLimitText by remember { mutableStateOf("120") }

    DisposableEffect(deviceId) {
        val loaded = deviceStore.loadAll().firstOrNull { it.deviceId == deviceId }
        var conn: AgentConnection? = null
        if (loaded != null) {
            conn = AgentConnection(loaded)
            connection = conn
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
            scope.launch {
                try {
                    conn.connect()
                } catch (e: Exception) {
                    statusMessage = "Не удалось подключиться к ПК."
                    statusIsError = true
                }
            }
        }

        onDispose { conn?.close() }
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Расписание") }) },
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(20.dp)
                .verticalScroll(rememberScrollState()),
        ) {
            Text(
                "Разрешённые дни и часы, плюс дневной лимит. Применяется одинаково ко всем выбранным дням.",
                style = MaterialTheme.typography.bodySmall,
            )

            Days.chunked(2).forEach { rowDays ->
                Row(verticalAlignment = Alignment.CenterVertically) {
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
                        if (ok) onBack()
                    }
                },
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp),
            ) { Text("Сохранить расписание") }

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
