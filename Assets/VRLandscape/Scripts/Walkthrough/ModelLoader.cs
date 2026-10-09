using System;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace VRLandscape
{
    // Loads a park's 3D model: the demo, a prefab in Resources (FBX imported in the editor),
    // a .glb/.gltf file at runtime via glTFast (Rhino 8 exports glTF directly), or an .obj (+ .mtl) file.
    public static class ModelLoader
    {
        public class Result : IDisposable
        {
            public GameObject Root;
            public Bounds Bounds;
            public GltfImport Gltf;
            public string Note;     // e.g. "scaled from millimetres", shown to the user

            public void Dispose()
            {
                Gltf?.Dispose();
                Gltf = null;
            }
        }

        public static async Task<Result> Load(ParkDef park, Transform parent)
        {
            var result = new Result();
            if (park.buildDemo != null)
            {
                result.Root = park.buildDemo(parent);
            }
            else if (!string.IsNullOrEmpty(park.resourcePrefab))
            {
                var prefab = Resources.Load<GameObject>(park.resourcePrefab);
                if (prefab == null) throw new Exception($"Prefab 'Resources/{park.resourcePrefab}' not found.");
                result.Root = UnityEngine.Object.Instantiate(prefab, parent, false);
            }
            else
            {
                string path = FileLoader.Combine(park.baseDir, park.model);
                if (path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                {
                    result.Root = await ObjLoader.Load(path, parent, park.upAxis?.Trim().ToLowerInvariant());
                    return Finish(park, result);
                }
                var gltf = new GltfImport();
                result.Gltf = gltf;
                bool ok;
                if (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = await FileLoader.ReadBytes(path);
                    if (bytes == null) throw new Exception($"Could not read model file {park.model}.");
                    ok = await gltf.Load(bytes, new Uri(FileLoader.ToUri(path)));
                }
                else
                {
                    ok = await gltf.Load(FileLoader.ToUri(path));
                }
                if (!ok) { result.Dispose(); throw new Exception($"glTF import failed for {park.model}."); }

                result.Root = new GameObject("Model");
                result.Root.transform.SetParent(parent, false);
                if (!await gltf.InstantiateMainSceneAsync(result.Root.transform))
                {
                    result.Dispose();
                    throw new Exception("glTF has no scene to show.");
                }
            }

            return Finish(park, result);
        }

        static Result Finish(ParkDef park, Result result)
        {
            result.Root.transform.localScale *= park.scale;

            // Rhino files modelled in millimetres arrive 1000x too big. A "park" over 3 km across is
            // almost certainly that, so convert to metres (unless park.json already sets a scale).
            if (park.buildDemo == null && Mathf.Approximately(park.scale, 1f))
            {
                var raw = ComputeBounds(result.Root);
                if (Mathf.Max(raw.size.x, raw.size.y, raw.size.z) > 3000f)
                {
                    result.Root.transform.localScale *= 0.001f;
                    result.Note = "This model looked like it was in millimetres, so it was scaled to metres.";
                }
            }
            if (park.collisions && !park.isDemo) AddColliders(result.Root);
            result.Bounds = ComputeBounds(result.Root);
            return result;
        }

        static void AddColliders(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                try { mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; }
                catch (Exception e) { Debug.LogWarning($"[VRLandscape] No collider for {mf.name}: {e.Message}"); }
            }
        }

        static Bounds ComputeBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // Feet position to start at: park.json "spawn" if given, otherwise the ground at the model's centre.
        public static Vector3 FindSpawn(ParkDef park, Bounds bounds, out float yaw)
        {
            yaw = park.HasSpawn ? park.spawn.yaw : 0f;
            if (park.HasSpawn) return ParkDef.ToVector(park.spawn.position, Vector3.zero);

            Physics.SyncTransforms();
            var top = new Vector3(bounds.center.x, bounds.max.y + 10f, bounds.center.z);
            if (Physics.Raycast(top, Vector3.down, out var hit, bounds.size.y + 20f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
    }
}
