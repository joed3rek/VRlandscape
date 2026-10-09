using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace VRLandscape
{
    // Finds parks in three places:
    //  1. The built-in procedural demos (Demo Park, Pocket Garden for AR).
    //  2. StreamingAssets/Parks/<folder>/park.json, listed in StreamingAssets/Parks/index.json (shipped in the APK).
    //  3. <persistentDataPath>/Parks/<folder>/park.json (copied onto the device over USB, no rebuild needed).
    public static class ParkCatalog
    {
        [Serializable]
        class ParkIndex { public string[] parks; }

        public static string DeviceParksFolder => Path.Combine(Application.persistentDataPath, "Parks");

        // Only parks in the device Parks folder (imported, or copied over USB) can be deleted.
        public static bool IsDeletable(ParkDef park)
        {
            if (park == null || park.isDemo || string.IsNullOrEmpty(park.baseDir)) return false;
            string root = DeviceParksFolder.Replace('\\', '/').TrimEnd('/') + "/";
            return park.baseDir.Replace('\\', '/').StartsWith(root);
        }

        public static void Delete(ParkDef park)
        {
            if (!IsDeletable(park)) throw new InvalidOperationException("Only imported parks can be deleted.");
            Directory.Delete(park.baseDir, true);
        }

        public static async Task<List<ParkDef>> LoadAll()
        {
            var parks = new List<ParkDef> { DemoPark.CreateDefinition(), PocketGarden.CreateDefinition() };

            string bundled = Application.streamingAssetsPath + "/Parks";
            string indexJson = await FileLoader.ReadText(bundled + "/index.json", quiet: true);
            if (indexJson != null)
            {
                var index = JsonUtility.FromJson<ParkIndex>(indexJson);
                if (index?.parks != null)
                    foreach (var folder in index.parks)
                    {
                        var park = await LoadPark(bundled + "/" + folder);
                        if (park != null) parks.Add(park);
                    }
            }

            foreach (var dir in DeviceParkFolders())
            {
                var park = await LoadPark(dir);
                if (park != null) parks.Add(park);
            }
            return parks;
        }

        static IEnumerable<string> DeviceParkFolders()
        {
            string[] dirs;
            try
            {
                Directory.CreateDirectory(DeviceParksFolder);
                string readme = Path.Combine(DeviceParksFolder, "README.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme,
                        "Put one folder per park here. Each folder needs a park.json (see the project README),\n" +
                        "plus the model (.glb) and/or 360 panorama images it references.\n");
                dirs = Directory.GetDirectories(DeviceParksFolder);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRLandscape] Cannot scan {DeviceParksFolder}: {e.Message}");
                yield break;
            }
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirs)
                if (File.Exists(Path.Combine(d, "park.json"))) yield return d.Replace('\\', '/');
        }

        static async Task<ParkDef> LoadPark(string folder)
        {
            string json = await FileLoader.ReadText(folder + "/park.json");
            if (json == null) return null;
            try
            {
                var park = JsonUtility.FromJson<ParkDef>(json);
                park.baseDir = folder;
                if (string.IsNullOrEmpty(park.name)) park.name = Path.GetFileName(folder.TrimEnd('/'));
                if (park.scale <= 0) park.scale = 1f;
                return park;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VRLandscape] Invalid park.json in {folder}: {e.Message}");
                return null;
            }
        }
    }
}
