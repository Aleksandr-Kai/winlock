package com.winlock.parent.ui

import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import com.winlock.parent.data.DeviceStore
import com.winlock.parent.model.PairedDevice
import com.winlock.parent.network.PairingClient
import com.winlock.parent.network.rootCauseMessage
import com.winlock.parent.protocol.PairingQrPayload
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AddDeviceScreen(deviceStore: DeviceStore, onDone: () -> Unit) {
    var manualText by remember { mutableStateOf("") }
    var controllerName by remember { mutableStateOf("Телефон (${Build.MODEL})") }
    var status by remember { mutableStateOf<String?>(null) }
    var isWorking by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    val scanLauncher = rememberLauncherForActivityResult(ScanContract()) { result ->
        val text = result.contents ?: return@rememberLauncherForActivityResult
        manualText = text
        status = null
    }

    fun pairWith(qrText: String) {
        val qr = PairingQrPayload.tryParse(qrText)
        if (qr == null) {
            status = "Не удалось разобрать QR-код."
            return
        }

        isWorking = true
        status = null
        scope.launch {
            try {
                val response = PairingClient.completePairing(qr, controllerName.ifBlank { "Родительское приложение" })
                val controllerId = response.controllerId
                if (!response.success || controllerId == null) {
                    status = "ПК отклонил привязку — возможно, QR-код устарел."
                    return@launch
                }

                deviceStore.add(
                    PairedDevice(
                        deviceId = qr.deviceId,
                        displayName = qr.deviceDisplayName,
                        controllerId = controllerId,
                        secretBase64 = java.util.Base64.getEncoder().encodeToString(qr.secret),
                        hostAndPort = qr.hostAndPort,
                        certificateFingerprintHex = qr.certificateFingerprintHex,
                    ),
                )
                onDone()
            } catch (e: Exception) {
                status = "Не удалось подключиться к ПК: ${e.rootCauseMessage()}"
            } finally {
                isWorking = false
            }
        }
    }

    Scaffold(topBar = { TopAppBar(title = { Text("Привязка ПК") }) }) { padding ->
        Column(
            modifier = Modifier
                .padding(padding)
                .padding(20.dp)
                .verticalScroll(rememberScrollState()),
        ) {
            Text(
                "Запустите на ПК ярлык «WinLock — Настройка» и отсканируйте QR-код камерой.",
                style = MaterialTheme.typography.bodyMedium,
            )

            Button(
                onClick = {
                    scanLauncher.launch(
                        ScanOptions()
                            .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                            .setBeepEnabled(false)
                            .setOrientationLocked(true)
                            .setPrompt("Наведите камеру на QR-код на экране ПК"),
                    )
                },
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp),
            ) {
                Text("Сканировать камерой")
            }

            Text(
                "...или вставьте текст QR-кода вручную ниже, если камера недоступна",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 16.dp),
            )

            OutlinedTextField(
                value = manualText,
                onValueChange = { manualText = it },
                label = { Text("winlock-pair:v1:...") },
                modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
                minLines = 3,
            )

            OutlinedTextField(
                value = controllerName,
                onValueChange = { controllerName = it },
                label = { Text("Название этого телефона") },
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp),
            )

            Button(
                onClick = { pairWith(manualText.trim()) },
                enabled = !isWorking && manualText.isNotBlank(),
                modifier = Modifier.fillMaxWidth().padding(top = 16.dp),
            ) {
                Text("Привязать")
            }

            if (isWorking) {
                CircularProgressIndicator(modifier = Modifier.size(24.dp).padding(top = 12.dp))
            }

            status?.let {
                Text(it, color = Color(0xFFE5484D), modifier = Modifier.padding(top = 12.dp))
            }
        }
    }
}
