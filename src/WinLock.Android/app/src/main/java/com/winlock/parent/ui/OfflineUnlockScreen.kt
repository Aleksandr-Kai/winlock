package com.winlock.parent.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import com.winlock.parent.crypto.OfflineUnlockCrypto
import com.winlock.parent.data.DeviceStore
import com.winlock.parent.protocol.OfflineUnlockChallenge
import java.util.Base64

/** Minutes granted by an offline unlock. Fixed, not chosen per-scan — the whole point of
 * this screen is "scan, read out the code, done" with nothing else to decide or type. */
private const val OfflineUnlockMinutes = 30

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun OfflineUnlockScreen(deviceId: String, deviceStore: DeviceStore, onBack: () -> Unit) {
    var code by remember { mutableStateOf<String?>(null) }
    var errorText by remember { mutableStateOf<String?>(null) }

    val scanLauncher = rememberLauncherForActivityResult(ScanContract()) { result ->
        val text = result.contents
        if (text == null) {
            // User backed out of the camera without scanning anything.
            onBack()
            return@rememberLauncherForActivityResult
        }

        val challenge = OfflineUnlockChallenge.tryParse(text)
        if (challenge == null) {
            errorText = "Не удалось разобрать QR-код с экрана ПК."
            return@rememberLauncherForActivityResult
        }

        val device = deviceStore.loadAll().firstOrNull { it.deviceId == challenge.deviceId }
        if (device == null) {
            errorText = "Этот QR-код не от одного из привязанных к этому телефону компьютеров."
            return@rememberLauncherForActivityResult
        }

        val secret = Base64.getDecoder().decode(device.secretBase64)
        code = OfflineUnlockCrypto.computeResponseCode(secret, challenge.challengeId, OfflineUnlockMinutes)
    }

    fun launchScan() {
        errorText = null
        scanLauncher.launch(
            ScanOptions()
                .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                .setBeepEnabled(false)
                .setOrientationLocked(true)
                .setPrompt("Наведите камеру на QR-код на экране ПК"),
        )
    }

    // Scans immediately on opening this screen — there's nothing to configure first.
    LaunchedEffect(Unit) { launchScan() }

    Scaffold(topBar = { TopAppBar(title = { Text("Оффлайн-разблокировка") }) }) { padding ->
        Box(
            modifier = Modifier.fillMaxSize().padding(padding).padding(20.dp),
            contentAlignment = Alignment.Center,
        ) {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                code?.let { value ->
                    Text("Добавит $OfflineUnlockMinutes мин. Назовите этот код:", style = MaterialTheme.typography.bodyMedium)
                    Card(modifier = Modifier.fillMaxWidth().padding(top = 12.dp)) {
                        Column(
                            modifier = Modifier.padding(24.dp),
                            horizontalAlignment = Alignment.CenterHorizontally,
                        ) {
                            Text(value, style = MaterialTheme.typography.displayMedium)
                        }
                    }
                    Button(onClick = onBack, modifier = Modifier.fillMaxWidth().padding(top = 20.dp)) {
                        Text("Назад")
                    }
                }

                errorText?.let { message ->
                    Text(message, color = Color(0xFFE5484D), textAlign = TextAlign.Center)
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(top = 16.dp)) {
                        Button(onClick = { launchScan() }) { Text("Сканировать снова") }
                        Button(onClick = onBack) { Text("Назад") }
                    }
                }
            }
        }
    }
}
