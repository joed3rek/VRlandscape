using System.Collections.Generic;
using UnityEngine;

namespace VRLandscape
{
    // Neo-brutalist design tokens: cream paper, pure black ink, highlighter colours,
    // 4px black borders, hard zero-blur offset shadows, Space Grotesk Bold.
    // Every UI element is styled from here so the look stays consistent and easy to change.
    public static class Theme
    {
        // Palette
        public static readonly Color Cream = Hex(0xFFFDF5);
        public static readonly Color Ink = Color.black;
        public static readonly Color Red = Hex(0xFF6B6B);      // primary action
        public static readonly Color Yellow = Hex(0xFFD93D);   // secondary / highlight
        public static readonly Color Violet = Hex(0xC4B5FD);   // tertiary
        public static readonly Color White = Color.white;

        // Structure (canvas units; reference resolution 1920 x 1080)
        public const int Border = 4;
        public static readonly Vector2 ShadowSm = new Vector2(4, -4);
        public static readonly Vector2 ShadowMd = new Vector2(8, -8);
        public static readonly Vector2 ShadowLg = new Vector2(12, -12);

        // Type scale
        public const int Display = 150, H1 = 88, H2 = 56, H3 = 40, BodyLg = 30, Body = 26, Small = 22;

        static Font font;
        static Sprite box;
        static Texture2D halftone, grid;
        static readonly Dictionary<int, Sprite> circles = new Dictionary<int, Sprite>();
        static readonly Dictionary<int, Sprite> stars = new Dictionary<int, Sprite>();

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.Load<Font>("VRLandscape/Fonts/SpaceGrotesk-Bold");
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        // White fill with a black border, 9-sliced. Image.color tints the fill; the border stays black.
        public static Sprite Box
        {
            get
            {
                if (box != null) return box;
                const int size = 16;
                var tex = NewTexture(size, size, FilterMode.Point);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        bool edge = x < Border || y < Border || x >= size - Border || y >= size - Border;
                        tex.SetPixel(x, y, edge ? Ink : White);
                    }
                tex.Apply(false, true);
                box = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
                return box;
            }
        }

        // Filled circle with a black ring, drawn at the exact size it is shown so the ring stays 4px.
        public static Sprite Circle(int diameter)
        {
            if (circles.TryGetValue(diameter, out var s) && s != null) return s;
            var tex = NewTexture(diameter, diameter, FilterMode.Bilinear);
            float r = diameter * 0.5f, inner = r - Border;
            for (int y = 0; y < diameter; y++)
                for (int x = 0; x < diameter; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float outerA = Mathf.Clamp01(r - d);
                    float fill = Mathf.Clamp01(inner - d);
                    var c = Color.Lerp(Ink, White, fill);
                    c.a = outerA;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, diameter, diameter), new Vector2(0.5f, 0.5f), 100f);
            circles[diameter] = s;
            return s;
        }

        // Five-point star sticker with a black outline.
        public static Sprite Star(int size)
        {
            if (stars.TryGetValue(size, out var s) && s != null) return s;
            var tex = NewTexture(size, size, FilterMode.Bilinear);
            var outer = StarPolygon(size * 0.5f, size * 0.48f);
            var inner = StarPolygon(size * 0.5f, size * 0.48f - Border * 1.6f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if (!Inside(outer, p)) { tex.SetPixel(x, y, Color.clear); continue; }
                    tex.SetPixel(x, y, Inside(inner, p) ? White : Ink);
                }
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            stars[size] = s;
            return s;
        }

        // Black halftone dots on transparent, tiled at 20 canvas units.
        public static Texture2D Halftone
        {
            get
            {
                if (halftone != null) return halftone;
                const int size = 20;
                halftone = NewTexture(size, size, FilterMode.Bilinear);
                halftone.wrapMode = TextureWrapMode.Repeat;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                        halftone.SetPixel(x, y, new Color(0, 0, 0, Mathf.Clamp01(2f - d)));
                    }
                halftone.Apply(false, true);
                return halftone;
            }
        }

        // Graph-paper lines every 40 canvas units.
        public static Texture2D Grid
        {
            get
            {
                if (grid != null) return grid;
                const int size = 40;
                grid = NewTexture(size, size, FilterMode.Point);
                grid.wrapMode = TextureWrapMode.Repeat;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        grid.SetPixel(x, y, x == 0 || y == 0 ? new Color(0, 0, 0, 0.1f) : Color.clear);
                grid.Apply(false, true);
                return grid;
            }
        }

        static Texture2D NewTexture(int w, int h, FilterMode filter)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
        }

        static Vector2[] StarPolygon(float c, float r)
        {
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float rad = (i % 2 == 0 ? r : r * 0.45f);
                float a = Mathf.PI / 2 + i * Mathf.PI / 5;
                pts[i] = new Vector2(c + Mathf.Cos(a) * rad, c + Mathf.Sin(a) * rad);
            }
            return pts;
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
