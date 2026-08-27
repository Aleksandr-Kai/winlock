package com.winlock.parent.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
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
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.winlock.parent.data.DeviceStore
import com.winlock.parent.model.PairedDevice
import com.winlock.parent.network.AgentConnection
import com.winlock.parent.network.DiscoveryClient
import kotlinx.coroutines.launch

/** Everything about a paired PC that isn't day-to-day control: fixing its address, and
 * unlinking it — moved off the main device screen so that one stays focused on the actions a
 * parent actually reaches for often. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeviceSettingsScreen(
    deviceId: String,
    deviceStore: DeviceStore,
    onBack: () -> Unit,
    onUnlinked: () -> Unit,
) {
    val scope = rememberCoroutineScope()
    val context = LocalContext.current
    var device by remember { mutableStateOf<PairedDevice?>(null) }
    var connection by remember { mutableStateOf<AgentConnection?>(null) }
    var hostAndPortText by remember { mutableStateOf("") }
    var statusMessage by remember { mutableStateOf<String?>(null) }
    var statusIsError by remember { mutableStateOf(false) }
    var showForgetDialog by remember { mutableStateOf(false) }

    fun startConnection(target: PairedDevice) {
        connection?.close()
        val conn = AgentConnection(target)
        connection = conn
        scope.launch {
            try {
                conn.connect()
            } catch (e: Exception) {
                // This screen doesn't show a live connection label — just used to verify a
                // freshly saved/discovered address actually works.
            }
        }
    }

    DisposableEffect(deviceId) {
        val loaded = deviceStore.loadAll().firstOrNull { it.deviceId == deviceId }
        device = loaded
        hostAndPortText = loaded?.hostAndPort ?: ""
        if (loaded != null) startConnection(loaded)

        onDispose { connection?.close() }
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
                    onUnlinked()
                }) { Text("Отвязать") }
            },
            dismissButton = {
                TextButton(onClick = { showForgetDialog = false }) { Text("Отмена") }
            },
        )
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text("Настройки") }) },
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(20.dp)
                .verticalScroll(rememberScrollState()),
        ) {
            Text("Подключение", style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(bottom = 4.dp))
            Text(
                "IP-адрес ПК может смениться (например, после перезагрузки роутера). Приложение " +
                    "само ищет его в локальной сети при каждом открытии списка устройств; если это " +
                    "не помогло — попробуйте найти именно этот ПК ниже, либо укажите адрес вручную.",
                style = MaterialTheme.typography.bodySmall,
            )
            Button(
                onClick = {
                    val current = device ?: return@Button
                    scope.launch {
                        statusMessage = "Ищем ПК в локальной сети..."
                        statusIsError = false
                        val discovered = DiscoveryClient(context).discover().firstOrNull { it.deviceId == current.deviceId }
                        if (discovered == null) {
                            statusMessage = "Не найден автоматически. Укажите IP вручную ниже, или используйте оффлайн-разблокировку."
                            statusIsError = true
                            return@launch
                        }

                        val updated = current.copy(hostAndPort = discovered.hostAndPort)
                        deviceStore.add(updated)
                        device = updated
                        hostAndPortText = updated.hostAndPort
                        startConnection(updated)
                        statusMessage = "Найден по адресу ${discovered.hostAndPort}, переподключаемся..."
                        statusIsError = false
                    }
                },
                modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
            ) { Text("Найти автоматически в сети") }
            OutlinedTextField(
                value = hostAndPortText,
                onValueChange = { hostAndPortText = it },
                label = { Text("IP:порт") },
                placeholder = { Text("192.168.1.50:51843") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
            )
            Button(
                onClick = {
                    val current = device
                    val trimmed = hostAndPortText.trim()
                    val port = trimmed.substringAfterLast(':', missingDelimiterValue = "").toIntOrNull()
                    if (current == null || port == null || trimmed.substringBeforeLast(':').isBlank()) {
                        statusMessage = "Укажите адрес в формате IP:порт, например 192.168.1.50:51843."
                        statusIsError = true
                        return@Button
                    }

                    val updated = current.copy(hostAndPort = trimmed)
                    deviceStore.add(updated)
                    device = updated
                    startConnection(updated)
                    statusMessage = "Адрес сохранён, переподключаемся..."
                    statusIsError = false
                },
                modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
            ) { Text("Сохранить и переподключиться") }

            statusMessage?.let {
                Text(
                    it,
                    color = if (statusIsError) Color(0xFFE5484D) else Color(0xFF5CE65C),
                    modifier = Modifier.padding(top = 16.dp),
                )
            }

            Button(
                onClick = { showForgetDialog = true },
                colors = ButtonDefaults.buttonColors(containerColor = Color(0xFFE5484D)),
                modifier = Modifier.fillMaxWidth().padding(top = 32.dp),
            ) { Text("Отвязать") }
        }
    }
}
