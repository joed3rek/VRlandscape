using UnityEngine;

namespace VRLandscape
{
    // A 10 x 10 m sample garden for first-time AR users: small enough to walk through life-size
    // in a backyard or parking lot. Entrance at z = 0, garden extends to z = 10 (x from -5 to 5).
    public static class PocketGarden
    {
        static readonly Color Lawn = new Color(0.33f, 0.56f, 0.24f);
        static readonly Color Gravel = new Color(0.80f, 0.74f, 0.62f);
        static readonly Color Hedge = new Color(0.17f, 0.36f, 0.16f);
        static readonly Color Bark = new Color(0.38f, 0.27f, 0.18f);
        static readonly Color Canopy = new Color(0.24f, 0.47f, 0.20f);
        static readonly Color CanopyLight = new Color(0.42f, 0.60f, 0.24f);
        static readonly Color Corten = new Color(0.55f, 0.30f, 0.17f);
        static readonly Color Timber = new Color(0.62f, 0.45f, 0.28f);
        static readonly Color Stone = new Color(0.74f, 0.73f, 0.70f);
        static readonly Color Water = new Color(0.24f, 0.50f, 0.70f);
        static readonly Color Pink = new Color(0.86f, 0.45f, 0.62f);
        static readonly Color Lavender = new Color(0.58f, 0.48f, 0.80f);
        static readonly Color Yellow = new Color(0.93f, 0.80f, 0.30f);

        public static ParkDef CreateDefinition()
        {
            return new ParkDef
            {
                name = "Pocket Garden",
                description = "Built-in 10 x 10 m garden. Try it in AR at life size in any open space",
                isDemo = true,
                buildDemo = Build,
                collisions = true,
                spawn = new SpawnDef { position = new[] { 0f, 0f, 0.3f }, yaw = 0f },
            };
        }

        public static GameObject Build(Transform parent)
        {
            var root = new GameObject("Pocket Garden").transform;
            root.SetParent(parent, false);

            // Ground: lawn base with a curving gravel path from the entrance to the pergola
            Box(root, new Vector3(0, -0.02f, 5), new Vector3(10, 0.04f, 10), Lawn);
            var path = new[] { new Vector3(0, 0, 0), new Vector3(0.7f, 0, 2.5f), new Vector3(-0.4f, 0, 5f), new Vector3(0.5f, 0, 7.5f), new Vector3(0, 0, 9.3f) };
            for (int i = 0; i < path.Length - 1; i++) PathSegment(root, path[i], path[i + 1], 1.2f);
            for (int i = 1; i < path.Length - 1; i++)
                Cyl(root, path[i] + new Vector3(0, 0.005f, 0), new Vector3(1.2f, 0.01f, 1.2f), Gravel, collider: false);

            // Side hedges (open entrance at the front)
            Box(root, new Vector3(-4.8f, 0.35f, 5.2f), new Vector3(0.4f, 0.7f, 9.6f), Hedge);
            Box(root, new Vector3(4.8f, 0.35f, 5.2f), new Vector3(0.4f, 0.7f, 9.6f), Hedge);
            Box(root, new Vector3(0, 0.35f, 9.8f), new Vector3(9.2f, 0.7f, 0.4f), Hedge);

            // Trees
            Tree(root, new Vector3(-3.0f, 0, 2.2f), 1.1f, false);
            Tree(root, new Vector3(3.3f, 0, 6.4f), 0.9f, true);
            Tree(root, new Vector3(-3.4f, 0, 8.8f), 0.8f, false);

            // Corten planters with shrubs
            Planter(root, new Vector3(2.9f, 0, 1.6f), 0);
            Planter(root, new Vector3(-2.6f, 0, 5.4f), 90);

            // Bench facing the path
            Bench(root, new Vector3(2.1f, 0, 4.2f), -90);

            // Round water basin
            Cyl(root, new Vector3(-1.9f, 0.22f, 7.2f), new Vector3(1.8f, 0.22f, 1.8f), Stone);
            Cyl(root, new Vector3(-1.9f, 0.41f, 7.2f), new Vector3(1.55f, 0.04f, 1.55f), Water, collider: false);

            // Pergola over the end of the path
            foreach (var x in new[] { -1.1f, 1.1f })
                foreach (var z in new[] { 8.5f, 9.5f })
                    Box(root, new Vector3(x, 1.3f, z), new Vector3(0.12f, 2.6f, 0.12f), Timber);
            for (int i = 0; i < 5; i++)
                Box(root, new Vector3(0, 2.65f, 8.3f + i * 0.35f), new Vector3(2.8f, 0.1f, 0.06f), Timber);
            Box(root, new Vector3(-1.1f, 2.55f, 9f), new Vector3(0.1f, 0.1f, 1.4f), Timber);
            Box(root, new Vector3(1.1f, 2.55f, 9f), new Vector3(0.1f, 0.1f, 1.4f), Timber);

            // Flowering perennials along the path edges
            var rng = new System.Random(3);
            var colors = new[] { Pink, Lavender, Yellow };
            for (int i = 0; i < 26; i++)
            {
                float z = 0.6f + i * 0.33f;
                float side = i % 2 == 0 ? 1 : -1;
                float pathX = PathX(path, z);
                var p = new Vector3(pathX + side * (0.95f + (float)rng.NextDouble() * 0.35f), 0, z);
                if (Mathf.Abs(p.x - 2.1f) < 0.6f && Mathf.Abs(p.z - 4.2f) < 1.1f) continue; // bench
                if ((p - new Vector3(-1.9f, 0, 7.2f)).magnitude < 1.1f) continue;             // basin
                float s = 0.28f + (float)rng.NextDouble() * 0.15f;
                Visuals.Primitive(PrimitiveType.Sphere, root, p + new Vector3(0, s * 0.45f, 0), new Vector3(s, s * 0.8f, s),
                    Visuals.Lit(colors[i % colors.Length]), collider: false);
            }
            return root.gameObject;
        }

        static float PathX(Vector3[] path, float z)
        {
            for (int i = 0; i < path.Length - 1; i++)
                if (z >= path[i].z && z <= path[i + 1].z)
                    return Mathf.Lerp(path[i].x, path[i + 1].x, (z - path[i].z) / (path[i + 1].z - path[i].z));
            return 0;
        }

        static void PathSegment(Transform root, Vector3 a, Vector3 b, float width)
        {
            var go = Box(root, (a + b) * 0.5f + new Vector3(0, 0.005f, 0), new Vector3(width, 0.01f, (b - a).magnitude), Gravel, collider: false);
            go.transform.localRotation = Quaternion.LookRotation(b - a);
        }

        static void Tree(Transform root, Vector3 pos, float size, bool light)
        {
            var crown = Visuals.Lit(light ? CanopyLight : Canopy);
            Cyl(root, pos + new Vector3(0, 1.4f * size, 0), new Vector3(0.22f, 1.4f, 0.22f) * size, Bark);
            Visuals.Primitive(PrimitiveType.Sphere, root, pos + new Vector3(0, 3.4f * size, 0), new Vector3(3.0f, 2.4f, 3.0f) * size, crown, collider: false);
            Visuals.Primitive(PrimitiveType.Sphere, root, pos + new Vector3(0.5f, 4.2f * size, 0.3f), new Vector3(2.0f, 1.8f, 2.0f) * size, crown, collider: false);
            Visuals.Primitive(PrimitiveType.Sphere, root, pos + new Vector3(-0.6f, 3.9f * size, -0.4f), new Vector3(1.8f, 1.6f, 1.8f) * size, crown, collider: false);
        }

        static void Planter(Transform root, Vector3 pos, float yaw)
        {
            var p = new GameObject("Planter").transform;
            p.SetParent(root, false);
            p.localPosition = pos;
            p.localRotation = Quaternion.Euler(0, yaw, 0);
            Box(p, new Vector3(0, 0.25f, 0), new Vector3(1.8f, 0.5f, 0.7f), Corten);
            for (int i = 0; i < 3; i++)
                Visuals.Primitive(PrimitiveType.Sphere, p, new Vector3(-0.55f + i * 0.55f, 0.75f, 0), new Vector3(0.6f, 0.55f, 0.6f),
                    Visuals.Lit(i == 1 ? Canopy : CanopyLight), collider: false);
        }

        static void Bench(Transform root, Vector3 pos, float yaw)
        {
            var b = new GameObject("Bench").transform;
            b.SetParent(root, false);
            b.localPosition = pos;
            b.localRotation = Quaternion.Euler(0, yaw, 0);
            Box(b, new Vector3(0, 0.45f, 0), new Vector3(1.6f, 0.07f, 0.45f), Timber);
            Box(b, new Vector3(0, 0.75f, 0.2f), new Vector3(1.6f, 0.35f, 0.05f), Timber);
            Box(b, new Vector3(-0.7f, 0.22f, 0), new Vector3(0.07f, 0.44f, 0.42f), Stone);
            Box(b, new Vector3(0.7f, 0.22f, 0), new Vector3(0.07f, 0.44f, 0.42f), Stone);
        }

        static GameObject Box(Transform root, Vector3 pos, Vector3 scale, Color color, bool collider = true) =>
            Visuals.Primitive(PrimitiveType.Cube, root, pos, scale, Visuals.Lit(color), collider);

        static GameObject Cyl(Transform root, Vector3 pos, Vector3 scale, Color color, bool collider = true) =>
            Visuals.Primitive(PrimitiveType.Cylinder, root, pos, scale, Visuals.Lit(color), collider);
    }
}
