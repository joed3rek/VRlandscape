using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace VRLandscape.EditorTools
{
    // Adds the permissions View Sync needs to the generated Android manifest:
    // Wi-Fi Direct rooms (Wi-Fi state, "Nearby devices") and local networking.
    public class AndroidManifestPatcher : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        static readonly string[] Permissions =
        {
            "<uses-permission android:name=\"android.permission.INTERNET\" />",
            "<uses-permission android:name=\"android.permission.ACCESS_WIFI_STATE\" />",
            "<uses-permission android:name=\"android.permission.CHANGE_WIFI_STATE\" />",
            "<uses-permission android:name=\"android.permission.ACCESS_NETWORK_STATE\" />",
            "<uses-permission android:name=\"android.permission.ACCESS_FINE_LOCATION\" />",
            "<uses-permission android:name=\"android.permission.NEARBY_WIFI_DEVICES\" android:usesPermissionFlags=\"neverForLocation\" />",
            "<uses-feature android:name=\"android.hardware.wifi.direct\" android:required=\"false\" />",
        };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest))
            {
                Debug.LogWarning("[VRLandscape] Android manifest not found at " + manifest);
                return;
            }
            string xml = File.ReadAllText(manifest);
            var insert = new System.Text.StringBuilder();
            foreach (var line in Permissions)
            {
                // Match on the permission/feature name so an existing entry isn't duplicated.
                int nameStart = line.IndexOf("android:name=\"") + 14;
                string name = line.Substring(nameStart, line.IndexOf('"', nameStart) - nameStart);
                if (!xml.Contains("\"" + name + "\"")) insert.Append("  ").Append(line).Append('\n');
            }
            if (insert.Length == 0) return;

            int at = xml.IndexOf("<application", System.StringComparison.Ordinal);
            if (at < 0) { Debug.LogWarning("[VRLandscape] No <application> in the Android manifest"); return; }
            xml = xml.Insert(at, insert.ToString());
            File.WriteAllText(manifest, xml);
        }
    }
}
