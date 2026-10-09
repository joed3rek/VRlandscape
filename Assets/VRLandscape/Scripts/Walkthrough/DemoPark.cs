using System.Collections.Generic;
using UnityEngine;

namespace VRLandscape
{
    // A procedural sample park, so the app works before any Rhino/Lumion content is added.
    // Its 360 panoramas are rendered at runtime from the same geometry (cubemaps).
    public static class DemoPark
    {
        static readonly Color Grass = new Color(0.30f, 0.52f, 0.22f);
        static readonly Color Path = new Color(0.78f, 0.70f, 0.55f);
        static readonly Color Stone = new Color(0.70f, 0.70f, 0.68f);
        static readonly Color Water = new Color(0.25f, 0.50f, 0.72f);
        static readonly Color Bark = new Color(0.36f, 0.25f, 0.16f);
        static readonly Color Leaves = new Color(0.20f, 0.42f, 0.18f);
        static readonly Color LeavesLight = new Color(0.36f, 0.55f, 0.20f);
        static readonly Color Hedge = new Color(0.16f, 0.34f, 0.15f);
        static readonly Color Wood = new Color(0.55f, 0.38f, 0.22f);
        static readonly Color Roof = new Color(0.45f, 0.22f, 0.18f);
        static readonly Color Metal = new Color(0.15f, 0.15f, 0.16f);

        public static ParkDef CreateDefinition()
        {
            return new ParkDef
            {
                name = "Demo Park",
                description = "Built-in sample: central fountain, pavilion, pond and tree-lined paths",
                isDemo = true,
                buildDemo = Build,
                spawn = new SpawnDef { position = new[] { 0f, 0f, -48f }, yaw = 0f },
                startPanorama = "entrance",
                panoramas = new[]
                {
                    Node("entrance", "Main Entrance", 0, -40, "fountain"),
                    Node("fountain", "Central Fountain", 0, -8, "entrance", "pavilion", "pond"),
                    Node("pavilion", "Pavilion Lawn", 26, 18, "fountain", "pond"),
                    Node("pond", "Pond Edge", -24, 22, "fountain", "pavilion"),
                }
            };
        }

        static PanoramaDef Node(string id, string name, float x, float z, params string[] links)
        {
            var hotspots = new List<HotspotDef>();
            foreach (var l in links) hotspots.Add(new HotspotDef { target = l, auto = true });
            return new PanoramaDef
            {
                id = id, name = name,
                position = new[] { x, PlayerRig.EyeHeight, z },
                hotspots = hotspots.ToArray()
            };
        }

        public static GameObject Build(Transform parent)
        {
            var root = new GameObject("Demo Park").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(7);

            // Ground and boundary hedge
            Visuals.Primitive(PrimitiveType.Cube, root, new Vector3(0, -0.5f, 0), new Vector3(120, 1, 120), Visuals.Lit(Grass));
            Box(root, new Vector3(0, 0.8f, 59.5f), new Vector3(120, 1.6f, 1), Hedge);
            Box(root, new Vector3(0, 0.8f, -59.5f), new Vector3(120, 1.6f, 1), Hedge);
            Box(root, new Vector3(59.5f, 0.8f, 0), new Vector3(1, 1.6f, 120), Hedge);
            Box(root, new Vector3(-59.5f, 0.8f, 0), new Vector3(1, 1.6f, 120), Hedge);

            // Paths: main axis, cross axis, and a ring around the fountain
            Box(root, new Vector3(0, 0.02f, -30), new Vector3(4, 0.04f, 60), Path, collider: false);
            Box(root, new Vector3(0, 0.02f, 0), new Vector3(100, 0.04f, 3), Path, collider: false);
            var ring = Visuals.Primitive(PrimitiveType.Cylinder, root, new Vector3(0, 0.01f, 0), new Vector3(20, 0.02f, 20), Visuals.Lit(Path));
            Object.Destroy(ring.GetComponent<Collider>());
            PathBetween(root, new Vector3(6, 0, 4), new Vector3(26, 0, 18));
            PathBetween(root, new Vector3(-6, 0, 4), new Vector3(-24, 0, 22));
            PathBetween(root, new Vector3(-24, 0, 22), new Vector3(26, 0, 18));

            // Fountain
            Cyl(root, new Vector3(0, 0.3f, 0), new Vector3(9, 0.3f, 9), Stone);
            Cyl(root, new Vector3(0, 0.45f, 0), new Vector3(8.2f, 0.2f, 8.2f), Water, collider: false);
            Cyl(root, new Vector3(0, 1.2f, 0), new Vector3(1, 1.2f, 1), Stone);
            Cyl(root, new Vector3(0, 2.4f, 0), new Vector3(3, 0.15f, 3), Stone);
            Cyl(root, new Vector3(0, 2.6f, 0), new Vector3(2.6f, 0.1f, 2.6f), Water, collider: false);

            // Pavilion
            var pav = new Vector3(30, 0, 24);
            for (int i = 0; i < 4; i++)
            {
                var c = pav + new Vector3(i % 2 == 0 ? -3 : 3, 1.5f, i < 2 ? -3 : 3);
                Cyl(root, c, new Vector3(0.4f, 1.5f, 0.4f), Stone);
            }
            Box(root, pav + new Vector3(0, 0.1f, 0), new Vector3(8, 0.2f, 8), Stone);
            Box(root, pav + new Vector3(0, 3.15f, 0), new Vector3(8, 0.3f, 8), Roof);
            Box(root, pav + new Vector3(0, 3.6f, 0), new Vector3(5, 0.6f, 5), Roof);

            // Pond
            var pond = new Vector3(-30, 0, 30);
            Cyl(root, pond + new Vector3(0, 0.02f, 0), new Vector3(16, 0.05f, 12), Water, collider: false);
            Cyl(root, pond + new Vector3(0, 0.01f, 0), new Vector3(17.5f, 0.04f, 13.5f), Stone, collider: false);

            // Benches and lamps along the main path
            for (int i = 0; i < 5; i++)
            {
                float z = -50 + i * 9;
                Bench(root, new Vector3(3.2f, 0, z), 90);
                Bench(root, new Vector3(-3.2f, 0, z), -90);
                Lamp(root, new Vector3(2.6f, 0, z + 4.5f));
                Lamp(root, new Vector3(-2.6f, 0, z + 4.5f));
            }

            // Avenue trees along the main path, then scattered trees away from paths and features
            for (int i = 0; i < 7; i++)
            {
                float z = -54 + i * 8;
                Tree(root, new Vector3(6, 0, z), 1f);
                Tree(root, new Vector3(-6, 0, z), 1f);
            }
            int placed = 0;
            for (int attempt = 0; attempt < 400 && placed < 70; attempt++)
            {
                var p = new Vector3((float)(rng.NextDouble() * 108 - 54), 0, (float)(rng.NextDouble() * 108 - 54));
                if (Mathf.Abs(p.x) < 9 && p.z < 3) continue;          // main avenue
                if (Mathf.Abs(p.z) < 4) continue;                      // cross path
                if (p.magnitude < 14) continue;                        // fountain plaza
                if ((p - pav).magnitude < 10) continue;
                if ((p - pond).magnitude < 13) continue;
                if (NearSegment(p, new Vector3(-24, 0, 22), new Vector3(26, 0, 18), 4)) continue;
                if (NearSegment(p, new Vector3(6, 0, 4), new Vector3(26, 0, 18), 4)) continue;
                if (NearSegment(p, new Vector3(-6, 0, 4), new Vector3(-24, 0, 22), 4)) continue;
                Tree(root, p, 0.8f + (float)rng.NextDouble() * 0.7f, rng.NextDouble() > 0.5);
                placed++;
            }
            return root.gameObject;
        }

        // Renders each panorama viewpoint of the demo into a cubemap.
        public static void RenderPanoramas(ParkDef park, Transform tempParent)
        {
            if (park.runtimeTextures != null) return;
            park.runtimeTextures = new Dictionary<string, Texture>();

            var geometry = Build(tempParent);
            var camGo = new GameObject("Panorama Capture Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1000f;
            if (RenderSettings.skybox == null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.78f, 0.92f);
            }

            foreach (var node in park.panoramas)
            {
                var rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32)
                {
                    dimension = UnityEngine.Rendering.TextureDimension.Cube,
                    name = "Demo Panorama " + node.id
                };
                rt.Create();
                camGo.transform.position = ParkDef.ToVector(node.position, Vector3.up * PlayerRig.EyeHeight);
                cam.RenderToCubemap(rt, 63);
                park.runtimeTextures[node.id] = rt;
            }

            Object.Destroy(camGo);
            geometry.SetActive(false);
            Object.Destroy(geometry);
        }

        static void Bench(Transform root, Vector3 pos, float yaw)
        {
            var b = new GameObject("Bench").transform;
            b.SetParent(root, false);
            b.localPosition = pos;
            b.localRotation = Quaternion.Euler(0, yaw, 0);
            Box(b, new Vector3(0, 0.45f, 0), new Vector3(1.8f, 0.08f, 0.5f), Wood);
            Box(b, new Vector3(0, 0.75f, 0.22f), new Vector3(1.8f, 0.4f, 0.06f), Wood);
            Box(b, new Vector3(-0.8f, 0.22f, 0), new Vector3(0.08f, 0.45f, 0.45f), Metal);
            Box(b, new Vector3(0.8f, 0.22f, 0), new Vector3(0.08f, 0.45f, 0.45f), Metal);
        }

        static void Lamp(Transform root, Vector3 pos)
        {
            Cyl(root, pos + new Vector3(0, 1.75f, 0), new Vector3(0.12f, 1.75f, 0.12f), Metal);
            var bulb = Visuals.Primitive(PrimitiveType.Sphere, root, pos + new Vector3(0, 3.6f, 0), Vector3.one * 0.4f,
                Visuals.Lit(new Color(1f, 0.95f, 0.8f)), collider: false);
            bulb.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Tree(Transform root, Vector3 pos, float size, bool light = false)
        {
            Cyl(root, pos + new Vector3(0, 1.5f * size, 0), new Vector3(0.35f, 1.5f, 0.35f) * size, Bark);
            var crown = light ? LeavesLight : Leaves;
            Visuals.Primitive(PrimitiveType.Sphere, root, pos + new Vector3(0, 4f * size, 0), new Vector3(3.6f, 3.2f, 3.6f) * size, Visuals.Lit(crown), collider: false);
            Visuals.Primitive(PrimitiveType.Sphere, root, pos + new Vector3(0.6f, 5.2f * size, 0.3f) * 1f, new Vector3(2.4f, 2.2f, 2.4f) * size, Visuals.Lit(crown), collider: false);
        }

        static void PathBetween(Transform root, Vector3 a, Vector3 b)
        {
            var mid = (a + b) * 0.5f + new Vector3(0, 0.02f, 0);
            var go = Box(root, mid, new Vector3(2.5f, 0.04f, (b - a).magnitude), Path, collider: false);
            go.transform.localRotation = Quaternion.LookRotation(b - a);
        }

        static GameObject Box(Transform root, Vector3 pos, Vector3 scale, Color color, bool collider = true) =>
            Visuals.Primitive(PrimitiveType.Cube, root, pos, scale, Visuals.Lit(color), collider);

        static GameObject Cyl(Transform root, Vector3 pos, Vector3 scale, Color color, bool collider = true) =>
            Visuals.Primitive(PrimitiveType.Cylinder, root, pos, scale, Visuals.Lit(color), collider);

        static bool NearSegment(Vector3 p, Vector3 a, Vector3 b, float distance)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude < distance;
        }
    }
}
