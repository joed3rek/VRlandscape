using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRLandscape
{
    // Data model for a park, loaded from park.json (see README / StreamingAssets/Parks/_Template).
    // Positions are [x, y, z] in metres, Unity space (Y up). Yaw is degrees clockwise from +Z.

    [Serializable]
    public class HotspotDef
    {
        public string target;   // id of the panorama this hotspot jumps to
        public string label;
        public float yaw;       // world heading of the hotspot, read it off the in-app "Info" readout
        public float pitch;     // degrees up (+) / down (-)
        public bool auto;       // true: aim at the target's position instead of using yaw/pitch
    }

    // Where a 360 render was taken on the real site, for On-site mode.
    // lat/lon from Google Maps (long-press), heading = compass bearing (0 = north, 90 = east) of the image centre.
    [Serializable]
    public class GeoDef
    {
        public double lat;
        public double lon;
        public float heading;

        public bool IsSet => lat != 0 || lon != 0;
    }

    [Serializable]
    public class PanoramaDef
    {
        public GeoDef geo;          // optional: real-world spot for On-site mode
        public string id;
        public string name;
        public string image;        // equirectangular 2:1 JPG/PNG, relative to the park folder
        public float[] position;    // optional: where the camera stood; shows a 360 marker in the 3D walkthrough
        public float yawOffset;     // world heading that the centre of the image faces
        public HotspotDef[] hotspots;
    }

    [Serializable]
    public class SpawnDef
    {
        public float[] position;    // feet position
        public float yaw;
    }

    [Serializable]
    public class ParkDef
    {
        public string name;
        public string description;
        public string model;            // .glb / .gltf / .obj relative to the park folder
        public string upAxis;           // OBJ only: "y" or "z"; empty = guess (flat site => thinnest axis is up)
        public string resourcePrefab;   // alternative: prefab under a Resources folder (FBX imported in the editor)
        public float scale = 1f;
        public bool collisions = true;
        public SpawnDef spawn;
        public PanoramaDef[] panoramas;
        public string startPanorama;

        [NonSerialized] public string baseDir;
        [NonSerialized] public bool isDemo;
        [NonSerialized] public Func<Transform, GameObject> buildDemo;   // procedural model for built-in demos
        [NonSerialized] public Dictionary<string, Texture> runtimeTextures;

        public bool HasModel => buildDemo != null || !string.IsNullOrEmpty(model) || !string.IsNullOrEmpty(resourcePrefab);
        public bool HasPanoramas => panoramas != null && panoramas.Length > 0;
        public bool HasSpawn => spawn != null && spawn.position != null && spawn.position.Length == 3;

        public PanoramaDef FindPanorama(string id)
        {
            if (panoramas == null) return null;
            foreach (var p in panoramas)
                if (p.id == id) return p;
            return null;
        }

        public string FirstPanoramaId()
        {
            if (!HasPanoramas) return null;
            if (!string.IsNullOrEmpty(startPanorama) && FindPanorama(startPanorama) != null) return startPanorama;
            return panoramas[0].id;
        }

        public static Vector3 ToVector(float[] a, Vector3 fallback)
        {
            return a != null && a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : fallback;
        }

        public static bool HasPosition(PanoramaDef p) => p.position != null && p.position.Length == 3;
    }
}
