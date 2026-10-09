package com.vrlandscape.sync;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.net.wifi.WifiManager;
import android.net.wifi.p2p.WifiP2pConfig;
import android.net.wifi.p2p.WifiP2pDevice;
import android.net.wifi.p2p.WifiP2pInfo;
import android.net.wifi.p2p.WifiP2pManager;
import android.net.wifi.p2p.nsd.WifiP2pDnsSdServiceInfo;
import android.net.wifi.p2p.nsd.WifiP2pDnsSdServiceRequest;
import android.os.Build;
import android.os.Looper;
import android.provider.Settings;

import java.util.HashMap;
import java.util.Map;

// Wi-Fi Direct "room" for View Sync.
//  - Presenter: creates a private local network (fixed name/passphrase from a 4-digit room code) and
//    advertises it with Wi-Fi Direct service discovery, so nearby phones see it without any setup.
//  - Viewer: discovers advertised rooms and joins one. No router, hotspot or mobile data needed.
// The presenter (group owner) is always at 192.168.49.1. Called from C# (VRLandscape.LocalGroup); state is polled.
public class LocalGroup {
    private static final String SERVICE_TYPE = "_vrlsync._tcp";
    private static final long ROOM_TTL_MS = 45000;

    private static WifiP2pManager manager;
    private static WifiP2pManager.Channel channel;
    private static volatile String state = "idle";   // idle, starting, hosting, joining, joined, failed
    private static volatile String error = "";
    private static boolean listenersSet;
    private static final Map<String, String> rooms = new HashMap<>();   // device address -> "room\tname\tpark"
    private static final Map<String, Long> roomSeen = new HashMap<>();

    public static String getState() { return state; }
    public static String getError() { return error; }

    private static void fail(String message) {
        error = message;
        state = "failed";
    }

    private static String reason(int code) {
        switch (code) {
            case WifiP2pManager.P2P_UNSUPPORTED: return "This phone doesn't support Wi-Fi Direct";
            case WifiP2pManager.BUSY: return "Wi-Fi is busy (turn the hotspot off) and try again";
            default: return "Wi-Fi Direct error " + code + ". Check that Wi-Fi is on and the hotspot is off";
        }
    }

    private static boolean ensure(Context context) {
        if (Build.VERSION.SDK_INT < 29) return false;
        if (manager == null) {
            Context app = context.getApplicationContext();
            manager = (WifiP2pManager) app.getSystemService(Context.WIFI_P2P_SERVICE);
            if (manager == null) return false;
            channel = manager.initialize(app, Looper.getMainLooper(), null);
        }
        return true;
    }

    private static boolean init(Context context) {
        if (Build.VERSION.SDK_INT < 29) { fail("Local rooms need Android 10 or newer"); return false; }
        if (!ensure(context)) { fail("This phone doesn't support Wi-Fi Direct"); return false; }
        return true;
    }

    private static WifiP2pConfig config(String networkName, String passphrase) {
        return new WifiP2pConfig.Builder()
                .setNetworkName(networkName)
                .setPassphrase(passphrase)
                .enablePersistentMode(false)
                .build();
    }

    // ------------------------------------------------------------ presenter

    public static void host(Context context, String networkName, String passphrase) {
        if (!init(context)) return;
        state = "starting";
        error = "";
        final WifiP2pConfig cfg = config(networkName, passphrase);
        try {
            manager.removeGroup(channel, new WifiP2pManager.ActionListener() {
                @Override public void onSuccess() { create(cfg); }
                @Override public void onFailure(int code) { create(cfg); }
            });
        } catch (SecurityException e) {
            fail("Allow \"Nearby devices\" so other phones can join");
        }
    }

    private static void create(WifiP2pConfig cfg) {
        try {
            manager.createGroup(channel, cfg, new WifiP2pManager.ActionListener() {
                @Override public void onSuccess() { state = "hosting"; }
                @Override public void onFailure(int code) { fail(reason(code)); }
            });
        } catch (SecurityException e) {
            fail("Allow \"Nearby devices\" so other phones can join");
        }
    }

    // Announces the room so nearby phones find it automatically (call again when the park changes).
    public static void advertise(Context context, String room, String name, String park) {
        if (!ensure(context)) return;
        Map<String, String> txt = new HashMap<>();
        txt.put("room", room);
        txt.put("name", trim(name));
        txt.put("park", trim(park));
        final WifiP2pDnsSdServiceInfo info = WifiP2pDnsSdServiceInfo.newInstance("VRLandscape-" + room, SERVICE_TYPE, txt);
        try {
            manager.clearLocalServices(channel, new WifiP2pManager.ActionListener() {
                @Override public void onSuccess() { addService(info); }
                @Override public void onFailure(int code) { addService(info); }
            });
        } catch (SecurityException ignored) { }
    }

    private static void addService(WifiP2pDnsSdServiceInfo info) {
        try {
            manager.addLocalService(channel, info, null);
            manager.discoverPeers(channel, null);   // stay discoverable
        } catch (SecurityException ignored) { }
    }

    // ------------------------------------------------------------ viewer

    // Looks for advertised rooms nearby; call every few seconds while the SYNC screen is open.
    public static void discover(Context context) {
        if (!ensure(context)) return;
        try {
            if (!listenersSet) {
                manager.setDnsSdResponseListeners(channel,
                        new WifiP2pManager.DnsSdServiceResponseListener() {
                            @Override public void onDnsSdServiceAvailable(String instanceName, String type, WifiP2pDevice device) { }
                        },
                        new WifiP2pManager.DnsSdTxtRecordListener() {
                            @Override public void onDnsSdTxtRecordAvailable(String domain, Map<String, String> record, WifiP2pDevice device) {
                                if (record == null || !record.containsKey("room") || device == null) return;
                                synchronized (rooms) {
                                    rooms.put(device.deviceAddress, record.get("room") + "\t" + nz(record.get("name")) + "\t" + nz(record.get("park")));
                                    roomSeen.put(device.deviceAddress, System.currentTimeMillis());
                                }
                            }
                        });
                listenersSet = true;
            }
            manager.clearServiceRequests(channel, new WifiP2pManager.ActionListener() {
                @Override public void onSuccess() { requestServices(); }
                @Override public void onFailure(int code) { requestServices(); }
            });
        } catch (SecurityException ignored) { }
    }

    private static void requestServices() {
        try {
            manager.addServiceRequest(channel, WifiP2pDnsSdServiceRequest.newInstance(), null);
            manager.discoverServices(channel, null);
        } catch (SecurityException ignored) { }
    }

    // Rooms seen recently, one per line: "room\tname\tpark".
    public static String getRooms() {
        StringBuilder sb = new StringBuilder();
        long now = System.currentTimeMillis();
        synchronized (rooms) {
            for (Map.Entry<String, String> e : rooms.entrySet()) {
                Long seen = roomSeen.get(e.getKey());
                if (seen == null || now - seen > ROOM_TTL_MS) continue;
                if (sb.length() > 0) sb.append('\n');
                sb.append(e.getValue());
            }
        }
        return sb.toString();
    }

    public static void stopDiscovery() {
        if (manager == null) return;
        try {
            manager.clearServiceRequests(channel, null);
            manager.stopPeerDiscovery(channel, null);
        } catch (SecurityException ignored) { }
    }

    public static void join(Context context, String networkName, String passphrase) {
        if (!init(context)) return;
        state = "joining";
        error = "";
        try {
            manager.connect(channel, config(networkName, passphrase), new WifiP2pManager.ActionListener() {
                @Override public void onSuccess() { /* wait for the group to form; see poll() */ }
                @Override public void onFailure(int code) { fail(reason(code)); }
            });
        } catch (SecurityException e) {
            fail("Allow \"Nearby devices\" to join");
        }
    }

    public static void poll() {
        if (manager == null || !"joining".equals(state)) return;
        try {
            manager.requestConnectionInfo(channel, new WifiP2pManager.ConnectionInfoListener() {
                @Override public void onConnectionInfoAvailable(WifiP2pInfo info) {
                    if (info != null && info.groupFormed && !info.isGroupOwner && "joining".equals(state)) state = "joined";
                }
            });
        } catch (SecurityException e) {
            fail("Allow \"Nearby devices\" to join");
        }
    }

    public static void stop() {
        if (manager != null) {
            try {
                manager.clearLocalServices(channel, null);
                manager.cancelConnect(channel, null);
                manager.removeGroup(channel, null);
            } catch (SecurityException ignored) { }
        }
        state = "idle";
        error = "";
    }

    // ------------------------------------------------------------ device checks

    public static boolean isWifiEnabled(Context context) {
        WifiManager wm = (WifiManager) context.getApplicationContext().getSystemService(Context.WIFI_SERVICE);
        return wm != null && wm.isWifiEnabled();
    }

    // Opens the quick Wi-Fi toggle panel (Android 10+) or the Wi-Fi settings page.
    public static void openWifiSettings(Activity activity) {
        Intent intent = Build.VERSION.SDK_INT >= 29 ? new Intent(Settings.Panel.ACTION_WIFI) : new Intent(Settings.ACTION_WIFI_SETTINGS);
        activity.startActivity(intent);
    }

    private static String trim(String s) {
        if (s == null) return "";
        return s.length() > 40 ? s.substring(0, 40) : s;
    }

    private static String nz(String s) { return s == null ? "" : s; }
}
