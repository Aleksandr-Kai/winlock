package com.winlock.parent.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.winlock.parent.data.DeviceStore
import com.winlock.parent.network.AgentConnection
import com.winlock.parent.protocol.NetTimeSpan
import com.winlock.parent.protocol.UsageHistoryDay
import kotlinx.coroutines.launch

private val MonthNames = listOf(
    "января", "февраля", "марта", "апреля", "мая", "июня",
    "июля", "августа", "сентября", "октября", "ноября", "декабря",
)

/** How much the PC was actually used each of the last several days — purely for a parent to
 * look back at, separate from the day-to-day controls on the main device screen. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeviceStatisticsScreen(
    deviceId: String,
    deviceStore: DeviceStore,
    onBack: () -> Unit,
) {
    val scope = rememberCoroutineScope()
    var connection by remember { mutableStateOf<AgentConnection?>(null) }
    var days by remember { mutableStateOf<List<UsageHistoryDay>>(emptyList()) }
    var statusMessage by remember { mutableStateOf<String?>(null) }

    DisposableEffect(deviceId) {
        val loaded = deviceStore.loadAll().firstOrNull { it.deviceId == deviceId }
        var conn: AgentConnection? = null
        if (loaded != null) {
            conn = AgentConnection(loaded)
            connection = conn
            conn.onUsageHistory = { snapshot -> days = snapshot.days }
            scope.launch {
                try {
                    conn.connect()
                } catch (e: Exception) {
                    statusMessage = "Не удалось подключиться к ПК."
                }
            }
        }

        onDispose { conn?.close() }
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Статистика") }) },
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(20.dp)
                .verticalScroll(rememberScrollState()),
        ) {
            Text(
                "Сколько времени компьютер был активно использован за последние дни.",
                style = MaterialTheme.typography.bodySmall,
            )

            statusMessage?.let {
                Text(it, color = Color(0xFFE5484D), modifier = Modifier.padding(top = 16.dp))
            }

            if (days.isEmpty() && statusMessage == null) {
                Text(
                    "Данные ещё не накопились.",
                    style = MaterialTheme.typography.bodyMedium,
                    modifier = Modifier.padding(top = 24.dp),
                )
            }

            days.sortedByDescending { it.date }.forEach { day ->
                Row(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 12.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(formatDate(day.date), style = MaterialTheme.typography.bodyMedium)
                    Text(
                        NetTimeSpan.parseToSeconds(day.usedTime)?.let { NetTimeSpan.formatHoursMinutes(it) } ?: "—",
                        style = MaterialTheme.typography.bodyMedium,
                    )
                }
                HorizontalDivider()
            }
        }
    }
}

/** "2026-09-04" -> "4 сентября". Falls back to the raw ISO string if it doesn't parse. */
private fun formatDate(isoDate: String): String {
    val parts = isoDate.split("-")
    val month = parts.getOrNull(1)?.toIntOrNull()?.minus(1)
    val day = parts.getOrNull(2)?.toIntOrNull()
    return if (month != null && day != null && month in MonthNames.indices) "$day ${MonthNames[month]}" else isoDate
}
