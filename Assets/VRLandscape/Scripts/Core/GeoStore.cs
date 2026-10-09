using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VRLandscape
{
    // Spot positions and headings saved on site ("Set here", "Save alignment").
    // Kept in one file on the device so it works for every park, including built-in and bundled ones;
    // a saved spot overrides the geo in park.json.
    public static class GeoStore
    {
        [Serializable] class Entry { public string key; public GeoDef geo; }
        [Serializable] class StoreFile { public List<Entry> entries = new List<Entry>(); }

        static Dictionary<string, GeoDef> spots;
        static string FilePath => Path.Combine(Application.persistentDataPath, "site_spots.json");

        static string Key(ParkDef park, PanoramaDef spot) => park.name + "/" + spot.id;

        public static GeoDef Get(ParkDef park, PanoramaDef spot)
        {
            Load();
            if (spots.TryGetValue(Key(park, spot), out var saved) && saved.IsSet) return saved;
            return spot.geo != null && spot.geo.IsSet ? spot.geo : null;
        }

        public static void Set(ParkDef park, PanoramaDef spot, GeoDef geo)
        {
            Load();
            spots[Key(park, spot)] = geo;
            var file = new StoreFile();
            foreach (var kv in spots) file.entries.Add(new Entry { key = kv.Key, geo = kv.Value });
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(file, true)); }
            catch (Exception e) { Debug.LogWarning("[VRLandscape] Could not save site spots: " + e.Message); }
        }

        static void Load()
        {
            if (spots != null) return;
            spots = new Dictionary<string, GeoDef>();
            try
            {
                if (!File.Exists(FilePath)) return;
                var file = JsonUtility.FromJson<StoreFile>(File.ReadAllText(FilePath));
                if (file?.entries != null)
                    foreach (var e in file.entries)
                        if (!string.IsNullOrEmpty(e.key) && e.geo != null) spots[e.key] = e.geo;
            }
            catch (Exception e) { Debug.LogWarning("[VRLandscape] Could not read site spots: " + e.Message); }
        }
    }
}
