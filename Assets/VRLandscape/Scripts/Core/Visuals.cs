using System.Collections.Generic;
using UnityEngine;

namespace VRLandscape
{
    // Shared materials and small primitive builders. Shaders live in Resources so they ship in builds.
    public static class Visuals
    {
        // GameObject.CreatePrimitive needs these components at runtime. Unity's docs: reference them in code,
        // or IL2CPP "Strip Engine Code" can remove them and CreatePrimitive fails on device (the demo parks are
        // built from primitives). See also Assets/link.xml.
        static readonly System.Type[] PrimitiveComponents =
        {
            typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider), typeof(SphereCollider),
            typeof(CapsuleCollider), typeof(MeshCollider)
        };

        static readonly Dictionary<Color, Material> litCache = new Dictionary<Color, Material>();
        static Material litTemplate;
        static Font font;

        public static Font Font => font != null ? font : (font = Theme.Font);

        public static Material Unlit(Color color)
        {
            var mat = new Material(Resources.Load<Shader>("VRLandscape/UnlitColor"));
            mat.color = color;
            return mat;
        }

        public static Material Panorama() => new Material(Resources.Load<Shader>("VRLandscape/Panorama"));

        // Lit material cloned from the built-in default material, so no shader lookup is needed in builds.
        public static Material Lit(Color color)
        {
            if (litCache.TryGetValue(color, out var cached) && cached != null) return cached;
            if (litTemplate == null)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                litTemplate = probe.GetComponent<Renderer>().sharedMaterial;
                Object.Destroy(probe);
            }
            var mat = new Material(litTemplate) { color = color };
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.1f);
            litCache[color] = mat;
            return mat;
        }

        // Unshared lit material (for loaded models that set their own texture).
        public static Material NewLit(Color color)
        {
            var mat = new Material(Lit(Color.white)) { color = color };
            return mat;
        }

        // Switches a Standard material to its "Fade" mode (glass, water with opacity).
        public static void MakeTransparent(Material mat)
        {
            if (!mat.HasProperty("_Mode")) return;
            mat.SetFloat("_Mode", 2f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        public static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        public static TextMesh Label(Transform parent, string text, Vector3 localPos, float size = 0.05f)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = Font;
            tm.fontSize = 64;
            tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            go.GetComponent<MeshRenderer>().sharedMaterial = Font.material;
            go.AddComponent<Billboard>();
            return tm;
        }
    }

    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var away = transform.position - cam.transform.position;
            if (away.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
