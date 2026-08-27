package com.winlock.parent.network

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
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
    private val nsdManager = context.applicationContext.getSystemService(Context.NSD_SERVICE) as NsdManager

    /** Browses for up to [timeoutMs], resolving whatever instances answer in that window.
     * Best-effort: a failed resolve for one instance doesn't lose the others, and this never
     * throws — an empty list just means nothing was found (or NSD isn't available), which the
     * caller treats the same as "keep using whatever address is already saved". */
    suspend fun discover(timeoutMs: Long = 4000L): List<DiscoveredDevice> {
        val found = ConcurrentLinkedQueue<DiscoveredDevice>()

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
        }

        return found.distinctBy { it.deviceId }
    }
}
