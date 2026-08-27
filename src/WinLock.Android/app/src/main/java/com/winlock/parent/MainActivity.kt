package com.winlock.parent

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.ui.Modifier
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.winlock.parent.data.DeviceStore
import com.winlock.parent.ui.AddDeviceScreen
import com.winlock.parent.ui.DeviceDetailScreen
import com.winlock.parent.ui.DeviceListScreen
import com.winlock.parent.ui.OfflineUnlockScreen

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val deviceStore = DeviceStore(applicationContext)

        setContent {
            MaterialTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    val navController = rememberNavController()

                    NavHost(navController = navController, startDestination = "devices") {
                        composable("devices") {
                            DeviceListScreen(
                                deviceStore = deviceStore,
                                onAddDevice = { navController.navigate("addDevice") },
                                onOpenDevice = { deviceId -> navController.navigate("device/$deviceId") },
                                onOfflineUnlock = { navController.navigate("offline") },
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
