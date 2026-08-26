package com.winlock.parent.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import com.winlock.parent.data.DeviceStore
import com.winlock.parent.model.PairedDevice

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeviceListScreen(
    deviceStore: DeviceStore,
    onAddDevice: () -> Unit,
    onOpenDevice: (String) -> Unit,
) {
    var devices by remember { mutableStateOf<List<PairedDevice>>(emptyList()) }

    // Reload every time this screen becomes visible again (e.g. after pairing a new device
    // and navigating back) — not just once on first composition.
    val lifecycleOwner = LocalLifecycleOwner.current
    androidx.compose.runtime.DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) {
                devices = deviceStore.loadAll()
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }

    Scaffold(
        topBar = { TopAppBar(title = { Text("WinLock") }) },
        floatingActionButton = {
            FloatingActionButton(onClick = onAddDevice) {
                Icon(Icons.Filled.Add, contentDescription = "Привязать ПК")
            }
        },
    ) { padding ->
        if (devices.isEmpty()) {
            Box(
                modifier = Modifier.fillMaxSize().padding(padding),
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
            LazyColumn(modifier = Modifier.fillMaxSize().padding(padding)) {
                items(devices, key = { it.deviceId }) { device ->
                    ListItem(
                        headlineContent = { Text(device.displayName) },
                        supportingContent = { Text(device.hostAndPort) },
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { onOpenDevice(device.deviceId) },
                    )
                }
            }
        }
    }
}
