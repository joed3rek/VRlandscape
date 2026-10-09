using System.Collections.Generic;
using UnityEngine;

namespace VRLandscape
{
    // Fades a whole model so the real site shows through. At full opacity the original materials are used;
    // below that, each material is swapped for a see-through copy with the same colour and texture.
    public class OpacityController : MonoBehaviour
    {
        Renderer[] renderers;
        Material[][] originals;
        readonly Dictionary<Material, Material> ghosts = new Dictionary<Material, Material>();
        float opacity = 1f;

        public float Opacity => opacity;

        public static OpacityController Attach(GameObject model)
        {
            var c = model.AddComponent<OpacityController>();
            c.renderers = model.GetComponentsInChildren<Renderer>();
            c.originals = new Material[c.renderers.Length][];
            for (int i = 0; i < c.renderers.Length; i++) c.originals[i] = c.renderers[i].sharedMaterials;
            return c;
        }

        public void SetOpacity(float value)
        {
            opacity = Mathf.Clamp01(value);
            bool solid = opacity > 0.99f;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r.GetComponent<TextMesh>() != null) continue;
                if (solid) { r.sharedMaterials = originals[i]; continue; }

                var mats = new Material[originals[i].Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = Ghost(originals[i][m]);
                r.sharedMaterials = mats;
            }
            foreach (var g in ghosts.Values) g.SetFloat("_Opacity", opacity);
        }

        Material Ghost(Material source)
        {
            if (source == null) return null;
            if (ghosts.TryGetValue(source, out var ghost)) return ghost;

            ghost = new Material(Resources.Load<Shader>("VRLandscape/Ghost"));
            if (source.HasProperty("_Color")) ghost.SetColor("_Color", source.GetColor("_Color"));
            else if (source.HasProperty("baseColorFactor")) ghost.SetColor("_Color", source.GetColor("baseColorFactor"));
            else if (source.HasProperty("_BaseColor")) ghost.SetColor("_Color", source.GetColor("_BaseColor"));

            Texture tex = null;
            if (source.HasProperty("_MainTex")) tex = source.GetTexture("_MainTex");
            if (tex == null && source.HasProperty("baseColorTexture")) tex = source.GetTexture("baseColorTexture");
            if (tex == null && source.HasProperty("_BaseMap")) tex = source.GetTexture("_BaseMap");
            if (tex != null) ghost.SetTexture("_MainTex", tex);

            ghosts[source] = ghost;
            return ghost;
        }

        void OnDestroy()
        {
            foreach (var g in ghosts.Values) if (g != null) Destroy(g);
        }
    }
}
