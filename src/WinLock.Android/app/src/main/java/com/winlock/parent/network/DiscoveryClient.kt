package com.winlock.parent.network

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.net.wifi.WifiManager
import com.winlock.parent.protocol.DiscoveryTxtRecord
import kotlinx.coroutines.delay
import java.util.concurrent.ConcurrentLinkedQueue

private const val ServiceType = "_winlock._tcp."

data class DiscoveredDevice(val deviceId: String, val hostAndPort: String)

/**
 * Finds WinLock PCs on the local network via mDNS/DNS-SD, so a paired phone can pick up a
 * PC's current address automatically after it changes (a new DHCP lease, a different Wi-Fi) —
 * without the parent re-scanning a QR code or typing the IP in by hand. Both of those remain
 * available; this is purely an additional, best-effort way to *find an address*. It never
 * establishes trust on its own — [network.CertificatePinning]'s pinned-fingerprint check
 * still gates every connection exactly as it does for a manually-entered address, so a rogue
 * device on the LAN answering the mDNS query can't get any further than a wrong IP would.
 * And because mDNS never crosses a router, this can't be used from outside the home network —
 * consistent with this app never controlling a PC over anything but the local network.
 */
class DiscoveryClient(context: Context) {
    private val appContext = context.applicationContext
    private val nsdManager = appContext.getSystemService(Context.NSD_SERVICE) as NsdManager
    private val wifiManager = appContext.getSystemService(Context.WIFI_SERVICE) as WifiManager

    /** Browses for up to [timeoutMs], resolving whatever instances answer in that window.
     * Best-effort: a failed resolve for one instance doesn't lose the others, and this never
     * throws — an empty list just means nothing was found (or NSD isn't available), which the
     * caller treats the same as "keep using whatever address is already saved". */
    suspend fun discover(timeoutMs: Long = 5000L): List<DiscoveredDevice> {
        val found = ConcurrentLinkedQueue<DiscoveredDevice>()

        // NsdManager is documented to not need this, but in practice — especially on
        // heavily-customized ROMs like MIUI — incoming multicast (what mDNS runs over) gets
        // silently dropped without an explicit WifiManager.MulticastLock held for the
        // duration of the scan, and NsdManager doesn't take one on the app's behalf. Without
        // this, discovery can run to completion, find nothing, and never surface an error —
        // it just looks like the PC isn't there.
        val multicastLock = wifiManager.createMulticastLock("winlock-discovery").apply {
            setReferenceCounted(true)
        }
        try {
            multicastLock.acquire()
        } catch (e: Exception) {
            // Best-effort — discovery still runs without it, just less reliably.
        }

        val discoveryListener = object : NsdManager.DiscoveryListener {
            override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {}
            override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) {}
            override fun onDiscoveryStarted(serviceType: String) {}
            override fun onDiscoveryStopped(serviceType: String) {}

            override fun onServiceFound(serviceInfo: NsdServiceInfo) {
                try {
                    // A fresh ResolveListener per call — NsdManager rejects reusing one that's
                    // still in flight, which a single shared instance would hit as soon as a
                    // second PC answers before the first resolve completes.
                    nsdManager.resolveService(
                        serviceInfo,
                        object : NsdManager.ResolveListener {
                            override fun onResolveFailed(info: NsdServiceInfo, errorCode: Int) {}
                            override fun onServiceResolved(info: NsdServiceInfo) {
                                val deviceId = DiscoveryTxtRecord.parseDeviceId(info.attributes) ?: return
                                val host = info.host?.hostAddress ?: return
                                found.add(DiscoveredDevice(deviceId, "$host:${info.port}"))
                            }
                        },
                    )
                } catch (e: Exception) {
                    // Resolve can legitimately fail to even start (service vanished between
                    // found and resolve) — skip this one instance, keep scanning.
                }
            }

            override fun onServiceLost(serviceInfo: NsdServiceInfo) {}
        }

        try {
            nsdManager.discoverServices(ServiceType, NsdManager.PROTOCOL_DNS_SD, discoveryListener)
            delay(timeoutMs)
        } catch (e: Exception) {
            // NSD can be unavailable on some devices/ROMs — fail safe to "nothing found".
        } finally {
            try {
                nsdManager.stopServiceDiscovery(discoveryListener)
            } catch (e: Exception) {
                // Already stopped or never started — nothing to clean up.
            }
            try {
                if (multicastLock.isHeld) multicastLock.release()
            } catch (e: Exception) {
                // Nothing more to do if release itself fails.
            }
        }

        return found.distinctBy { it.deviceId }
    }
}
