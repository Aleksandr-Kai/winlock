package com.winlock.parent

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.winlock.parent.data.DeviceStore
import com.winlock.parent.data.ThemeStore
import com.winlock.parent.ui.AddDeviceScreen
import com.winlock.parent.ui.DeviceDetailScreen
import com.winlock.parent.ui.DeviceListScreen
import com.winlock.parent.ui.DeviceScheduleScreen
import com.winlock.parent.ui.DeviceSettingsScreen
import com.winlock.parent.ui.OfflineUnlockScreen
import com.winlock.parent.ui.theme.WinLockTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val deviceStore = DeviceStore(applicationContext)
        val themeStore = ThemeStore(applicationContext)

        setContent {
            var themeMode by remember { mutableStateOf(themeStore.load()) }

            WinLockTheme(themeMode) {
                Surface(modifier = Modifier.fillMaxSize()) {
                    val navController = rememberNavController()

                    NavHost(navController = navController, startDestination = "devices") {
                        composable("devices") {
                            DeviceListScreen(
                                deviceStore = deviceStore,
                                onAddDevice = { navController.navigate("addDevice") },
                                onOpenDevice = { deviceId -> navController.navigate("device/$deviceId") },
                                onOfflineUnlock = { navController.navigate("offline") },
                                themeMode = themeMode,
                                onThemeModeChange = { mode ->
                                    themeMode = mode
                                    themeStore.save(mode)
                                },
                            )
                        }
                        composable("addDevice") {
                            AddDeviceScreen(
                                deviceStore = deviceStore,
                                onDone = { navController.popBackStack() },
                            )
                        }
                        composable(
                            "device/{deviceId}",
                            arguments = listOf(navArgument("deviceId") { type = NavType.StringType }),
                        ) { backStackEntry ->
                            val deviceId = backStackEntry.arguments?.getString("deviceId") ?: return@composable
                            DeviceDetailScreen(
                                deviceId = deviceId,
                                deviceStore = deviceStore,
                                onBack = { navController.popBackStack() },
                                onOpenSettings = { navController.navigate("device/$deviceId/settings") },
                                onOpenSchedule = { navController.navigate("device/$deviceId/schedule") },
                            )
                        }
                        composable(
                            "device/{deviceId}/settings",
                            arguments = listOf(navArgument("deviceId") { type = NavType.StringType }),
                        ) { backStackEntry ->
                            val deviceId = backStackEntry.arguments?.getString("deviceId") ?: return@composable
                            DeviceSettingsScreen(
                                deviceId = deviceId,
                                deviceStore = deviceStore,
                                onBack = { navController.popBackStack() },
                                onUnlinked = { navController.popBackStack("devices", false) },
                            )
                        }
                        composable(
                            "device/{deviceId}/schedule",
                            arguments = listOf(navArgument("deviceId") { type = NavType.StringType }),
                        ) { backStackEntry ->
                            val deviceId = backStackEntry.arguments?.getString("deviceId") ?: return@composable
                            DeviceScheduleScreen(
                                deviceId = deviceId,
                                deviceStore = deviceStore,
                                onBack = { navController.popBackStack() },
                            )
                        }
                        composable("offline") {
                            OfflineUnlockScreen(
                                deviceStore = deviceStore,
                                onBack = { navController.popBackStack() },
                            )
                        }
                    }
                }
            }
        }
    }
}
