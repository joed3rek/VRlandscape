using System.Collections.Generic;
using UnityEngine;

namespace VRLandscape
{
    // C# side of the Wi-Fi Direct room (Assets/Plugins/Android/LocalGroup.java).
    // A 4-digit room code maps to a fixed network name and passphrase; rooms are also advertised so
    // nearby phones find them without typing anything.
    public static class LocalGroup
    {
        public const string PresenterAddress = "192.168.49.1";   // Android's Wi-Fi Direct group owner

        public struct Room { public string code, name, park; }

        public static bool Supported =>
#if UNITY_ANDROID && !UNITY_EDITOR
            true;
#else
            false;
#endif

        public static string NetworkName(string code) => "DIRECT-VL-Park-" + code;
        static string Passphrase(string code) => "landscape-" + code + "-vr";

        public static string State => Call<string>("getState") ?? "unsupported";
        public static string Error => Call<string>("getError") ?? "";

        public static void Host(string code)
        {
            RequestPermissions();
            CallWithContext("host", NetworkName(code), Passphrase(code));
        }

        public static void Advertise(string code, string name, string park) => CallWithContext("advertise", code, name, park);

        public static void Join(string code)
        {
            RequestPermissions();
            CallWithContext("join", NetworkName(code), Passphrase(code));
        }

        public static void Discover() => CallWithContext("discover");
        public static void StopDiscovery() => CallVoid("stopDiscovery");
        public static void Poll() => CallVoid("poll");
        public static void Stop() => CallVoid("stop");

        public static List<Room> Rooms()
        {
            var list = new List<Room>();
            string raw = Call<string>("getRooms");
            if (string.IsNullOrEmpty(raw)) return list;
            foreach (var line in raw.Split('\n'))
            {
                var p = line.Split('\t');
                if (p.Length >= 3 && p[0].Length > 0) list.Add(new Room { code = p[0], name = p[1], park = p[2] });
            }
            return list;
        }

        // ------------------------------------------------------------ readiness checks

        public static bool WifiOn
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try { return Plugin.CallStatic<bool>("isWifiEnabled", Activity); } catch { return true; }
#else
                return true;
#endif
            }
        }

        public static void OpenWifiSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { Plugin.CallStatic("openWifiSettings", Activity); }
            catch (System.Exception e) { Debug.LogWarning("[VRLandscape] Wi-Fi settings: " + e.Message); }
#endif
        }

        // Android 13+ uses "Nearby devices"; older versions need location for Wi-Fi Direct.
        public static bool HasPermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return SdkInt >= 33
                    ? UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.NEARBY_WIFI_DEVICES")
                    : UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation);
#else
                return true;
#endif
            }
        }

        public static void RequestPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            UnityEngine.Android.Permission.RequestUserPermissions(new[]
            {
                UnityEngine.Android.Permission.FineLocation,
                "android.permission.NEARBY_WIFI_DEVICES"
            });
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaClass plugin;
        static AndroidJavaClass Plugin => plugin ?? (plugin = new AndroidJavaClass("com.vrlandscape.sync.LocalGroup"));

        static AndroidJavaObject Activity =>
            new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity");

        static int sdkInt;
        static int SdkInt => sdkInt != 0 ? sdkInt : (sdkInt = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT"));

        static T Call<T>(string method)
        {
            try { return Plugin.CallStatic<T>(method); }
            catch (System.Exception e) { Debug.LogWarning("[VRLandscape] LocalGroup." + method + ": " + e.Message); return default; }
        }

        static void CallVoid(string method)
        {
            try { Plugin.CallStatic(method); }
            catch (System.Exception e) { Debug.LogWarning("[VRLandscape] LocalGroup." + method + ": " + e.Message); }
        }

        static void CallWithContext(string method, params string[] args)
        {
            try
            {
                var all = new object[args.Length + 1];
                all[0] = Activity;
                for (int i = 0; i < args.Length; i++) all[i + 1] = args[i];
                Plugin.CallStatic(method, all);
            }
            catch (System.Exception e) { Debug.LogWarning("[VRLandscape] LocalGroup." + method + ": " + e.Message); }
        }
#else
        static T Call<T>(string method) => default;
        static void CallVoid(string method) { }
        static void CallWithContext(string method, params string[] args) { }
#endif
    }
}
