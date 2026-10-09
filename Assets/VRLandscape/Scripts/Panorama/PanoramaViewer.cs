using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace VRLandscape
{
    // 360 tour: shows one panorama at a time around the viewer's head, with hotspots to jump between them.
    public class PanoramaViewer : MonoBehaviour
    {
        const float SphereRadius = 50f;
        const float HotspotDistance = 6f;
        const int MaxCachedTextures = 3;

        public event Action<PanoramaDef> NodeChanged;
        public PanoramaDef Current { get; private set; }

        ParkDef park;
        Camera cam;
        Material material;
        Transform sphere, hotspotRoot;
        bool busy;
        readonly Dictionary<string, Texture> cache = new Dictionary<string, Texture>();
        readonly LinkedList<string> cacheOrder = new LinkedList<string>();

        public static PanoramaViewer Create(Transform parent, ParkDef park, Camera cam)
        {
            var go = new GameObject("Panorama Viewer");
            go.transform.SetParent(parent, false);
            var viewer = go.AddComponent<PanoramaViewer>();
            viewer.park = park;
            viewer.cam = cam;
            viewer.material = Visuals.Panorama();
            viewer.material.SetFloat("_Fade", 0);
            viewer.sphere = Visuals.Primitive(PrimitiveType.Sphere, go.transform, Vector3.zero,
                Vector3.one * SphereRadius * 2f, viewer.material, collider: false).transform;
            viewer.sphere.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            viewer.hotspotRoot = new GameObject("Hotspots").transform;
            viewer.hotspotRoot.SetParent(go.transform, false);
            return viewer;
        }

        public async Task ShowNode(string id)
        {
            var node = park.FindPanorama(id);
            if (node == null || busy) return;
            busy = true;
            try
            {
                await Fade(1, 0, 0.2f);
                var texture = await GetTexture(node);
                if (this == null) return;

                if (texture is RenderTexture rt && rt.dimension == UnityEngine.Rendering.TextureDimension.Cube)
                {
                    material.SetTexture("_Cube", rt);
                    material.SetFloat("_UseCube", 1);
                }
                else
                {
                    material.SetTexture("_MainTex", texture);
                    material.SetFloat("_UseCube", 0);
                    // A 2:1 image covers 180 degrees vertically; wider images cover proportionally less.
                    float aspect = texture.height > 0 ? (float)texture.width / texture.height : 2f;
                    material.SetFloat("_VCover", Mathf.Min(180f, 360f / Mathf.Max(aspect, 0.01f)));
                }
                material.SetFloat("_Yaw", node.yawOffset);
                Current = node;
                BuildHotspots(node);
                NodeChanged?.Invoke(node);
                await Fade(0, 1, 0.25f);
            }
            finally
            {
                busy = false;
            }
        }

        void LateUpdate()
        {
            // The panorama is infinitely far away: keep it (and its hotspots) centred on the head.
            var head = cam.transform.position;
            sphere.position = head;
            hotspotRoot.position = head;
        }

        async Task<Texture> GetTexture(PanoramaDef node)
        {
            if (park.runtimeTextures != null && park.runtimeTextures.TryGetValue(node.id, out var runtime)) return runtime;

            if (cache.TryGetValue(node.id, out var cached))
            {
                cacheOrder.Remove(node.id);
                cacheOrder.AddFirst(node.id);
                return cached;
            }

            var tex = await FileLoader.ReadTexture(FileLoader.Combine(park.baseDir, node.image));
            if (tex == null) return Texture2D.grayTexture;
            else
            {
                cache[node.id] = tex;
                cacheOrder.AddFirst(node.id);
                while (cacheOrder.Count > MaxCachedTextures)
                {
                    var oldest = cacheOrder.Last.Value;
                    cacheOrder.RemoveLast();
                    if (oldest != node.id && cache.TryGetValue(oldest, out var old)) Destroy(old);
                    cache.Remove(oldest);
                }
            }
            return tex;
        }

        void BuildHotspots(PanoramaDef node)
        {
            foreach (Transform child in hotspotRoot) Destroy(child.gameObject);
            if (node.hotspots == null) return;

            foreach (var h in node.hotspots)
            {
                var target = park.FindPanorama(h.target);
                if (target == null) continue;

                float yaw = h.yaw, pitch = h.pitch;
                if (h.auto && ParkDef.HasPosition(node) && ParkDef.HasPosition(target))
                    AimAt(ParkDef.ToVector(node.position, Vector3.zero), ParkDef.ToVector(target.position, Vector3.zero), out yaw, out pitch);

                var dir = Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward;
                var marker = CreateMarker(hotspotRoot, dir * HotspotDistance,
                    string.IsNullOrEmpty(h.label) ? (target.name ?? target.id) : h.label,
                    Theme.Yellow, 0.45f);
                string targetId = target.id;
                marker.onActivate = () => _ = ShowNode(targetId);
            }
        }

        public static void AimAt(Vector3 from, Vector3 to, out float yaw, out float pitch)
        {
            var d = to - from;
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float horizontal = new Vector2(d.x, d.z).magnitude;
            // Aim slightly below the horizon so markers sit on the ground-ish, like a path arrow.
            pitch = Mathf.Clamp(Mathf.Atan2(d.y, horizontal) * Mathf.Rad2Deg - 8f, -60f, 60f);
        }

        // Shared by hotspots in the 360 view and the 360 markers in the 3D walkthrough.
        public static PointerTarget CreateMarker(Transform parent, Vector3 localPos, string label, Color color, float size)
        {
            var go = new GameObject("Marker " + label);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var mat = Visuals.Unlit(color);
            var ball = Visuals.Primitive(PrimitiveType.Sphere, go.transform, Vector3.zero, Vector3.one * size, mat);
            ball.GetComponent<Collider>().isTrigger = true;
            ball.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ring = Visuals.Primitive(PrimitiveType.Sphere, go.transform, Vector3.zero, Vector3.one * size * 1.5f,
                Visuals.Unlit(new Color(color.r, color.g, color.b, 0.25f)), collider: false);
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Visuals.Label(go.transform, label, new Vector3(0, -size * 1.4f, 0), size * 0.12f);

            var target = go.AddComponent<PointerTarget>();
            var baseColor = color;
            target.onHover = h =>
            {
                mat.color = h ? Color.white : baseColor;
                go.transform.localScale = Vector3.one * (h ? 1.25f : 1f);
            };
            go.AddComponent<MarkerPulse>().ring = ring.transform;
            return target;
        }

        async Task Fade(float from, float to, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                if (this == null) return;
                material.SetFloat("_Fade", Mathf.Lerp(from, to, t / seconds));
                await Task.Yield();
            }
            if (this != null) material.SetFloat("_Fade", to);
        }

        void OnDestroy()
        {
            foreach (var tex in cache.Values) if (tex != null) Destroy(tex);
            cache.Clear();
            if (material != null) Destroy(material);
        }
    }

    public class MarkerPulse : MonoBehaviour
    {
        public Transform ring;
        Vector3 baseScale;

        void Start() => baseScale = ring.localScale;

        void Update()
        {
            ring.localScale = baseScale * (1f + 0.15f * Mathf.Sin(Time.time * 3f));
        }
    }
}
