package com.winlock.parent.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import com.winlock.parent.data.DeviceStore
import com.winlock.parent.model.PairedDevice
import com.winlock.parent.network.DiscoveryClient
import kotlinx.coroutines.delay

private val OnlineColor = Color(0xFF5CE65C)
private val OfflineColor = Color(0xFFE5484D)
private val ScanInterval = 15_000L

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeviceListScreen(
    deviceStore: DeviceStore,
    onAddDevice: () -> Unit,
    onOpenDevice: (String) -> Unit,
    onOfflineUnlock: () -> Unit,
) {
    var devices by remember { mutableStateOf<List<PairedDevice>>(emptyList()) }
    var onlineDeviceIds by remember { mutableStateOf<Set<String>>(emptySet()) }

    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current

    // Reloads the list and re-scans the network on every resume, then keeps re-scanning every
    // 15s for as long as the screen stays in the foreground — repeatOnLifecycle pauses this
    // automatically while backgrounded, so it doesn't burn battery/radio when nobody's
    // looking at the screen. Each scan does two things: refreshes the online/offline dot for
    // every paired device, and silently fixes any drifted IP (new DHCP lease, different
    // Wi-Fi) — best-effort only; manual IP entry and QR re-pairing both still work exactly as
    // before if a PC never answers.
    LaunchedEffect(lifecycleOwner) {
        lifecycleOwner.lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
            while (true) {
                devices = deviceStore.loadAll()

                val found = DiscoveryClient(context).discover()
                onlineDeviceIds = found.map { it.deviceId }.toSet()

                var anyUpdated = false
                deviceStore.loadAll().forEach { paired ->
                    val discovered = found.firstOrNull { it.deviceId == paired.deviceId }
                    if (discovered != null && discovered.hostAndPort != paired.hostAndPort) {
                        deviceStore.add(paired.copy(hostAndPort = discovered.hostAndPort))
                        anyUpdated = true
                    }
                }
                if (anyUpdated) devices = deviceStore.loadAll()

                delay(ScanInterval)
            }
        }
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text("WinLock") }) },
        floatingActionButton = {
            FloatingActionButton(onClick = onAddDevice) {
                Icon(Icons.Filled.Add, contentDescription = "Привязать ПК")
            }
        },
    ) { padding ->
        Column(modifier = Modifier.fillMaxSize().padding(padding)) {
            // Not tied to any one paired PC — this is the emergency path for when a
            // computer has no network at all, so it has to be reachable without first
            // picking a device.
            Button(
                onClick = onOfflineUnlock,
                modifier = Modifier.fillMaxWidth().padding(16.dp),
            ) { Text("Оффлайн-разблокировка") }

            if (devices.isEmpty()) {
                Box(
                    modifier = Modifier.weight(1f).fillMaxWidth(),
                    contentAlignment = Alignment.Center,
                ) {
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Text("Пока нет привязанных компьютеров")
                        Text("Нажмите + и отсканируйте QR-код на ПК")
                    }
                }
            } else {
                LazyColumn(modifier = Modifier.weight(1f).fillMaxWidth()) {
                    items(devices, key = { it.deviceId }) { device ->
                        ListItem(
                            headlineContent = { Text(device.displayName) },
                            supportingContent = { Text(device.hostAndPort) },
                            trailingContent = {
                                val online = device.deviceId in onlineDeviceIds
                                Box(
                                    modifier = Modifier
                                        .size(12.dp)
                                        .background(if (online) OnlineColor else OfflineColor, CircleShape),
                                )
                            },
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable { onOpenDevice(device.deviceId) },
                        )
                    }
                }
            }
        }
    }
}
