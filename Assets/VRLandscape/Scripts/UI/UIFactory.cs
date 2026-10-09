using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRLandscape
{
    // Builds the neo-brutalist uGUI in code (no prefabs). Styling comes from Theme.
    // Building blocks: Card, Button (push-down), Badge/Sticker, Pattern, Marquee, decorative shapes.
    public static class UIFactory
    {
        // Kept for callers that pick colours by role.
        public static Color PanelColor => Theme.Cream;
        public static Color ButtonColor => Theme.Red;
        public static Color SecondaryButtonColor => Theme.White;
        public static Color TextColor => Theme.Ink;
        public static Color MutedText => Theme.Ink;

        // ------------------------------------------------------------ canvases & layout

        // World canvases use the same 1920 x 1080 layout as phones: about 2.1 m wide in VR.
        public static Canvas CreateCanvas(string name, bool world, Vector2 worldSize = default)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            var scaler = go.AddComponent<CanvasScaler>();
            if (world)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = worldSize == default ? new Vector2(1920, 1080) : worldSize;
                rt.localScale = Vector3.one * 0.0011f;
                scaler.dynamicPixelsPerUnit = 3f;
                canvas.worldCamera = Camera.main;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 1f;
                go.AddComponent<GraphicRaycaster>();
            }
            return canvas;
        }

        public static void Stretch(RectTransform rt, float padding = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        // Places a rect by anchor point (0..1) with a size and offset from that anchor.
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        public static LayoutElement Layout(Component c, float preferredWidth = -1, float preferredHeight = -1, float flexibleWidth = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = preferredWidth;
            le.preferredHeight = preferredHeight;
            le.flexibleWidth = flexibleWidth;
            return le;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // ------------------------------------------------------------ surfaces

        // Plain, borderless fill (full-screen backgrounds, invisible touch layers).
        public static RectTransform Panel(Transform parent, Color color, string name = "Panel")
        {
            var rt = Rect(parent, name);
            rt.gameObject.AddComponent<Image>().color = color;
            Stretch(rt);
            return rt;
        }

        // Bordered "paper" block with a hard offset shadow.
        public static RectTransform Card(Transform parent, Color fill, Vector2 shadow, string name = "Card")
        {
            var rt = Rect(parent, name);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = Theme.Box;
            image.type = Image.Type.Sliced;
            image.color = fill;
            if (shadow != Vector2.zero) HardShadow(image, shadow);
            Stretch(rt);
            return rt;
        }

        public static Shadow HardShadow(Graphic g, Vector2 offset)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = Theme.Ink;
            s.effectDistance = offset;
            s.useGraphicAlpha = true;
            return s;
        }

        // Tiled texture overlay (halftone, grid) that keeps its tile size as the rect resizes.
        public static RawImage Pattern(Transform parent, Texture2D texture, float tile, float opacity = 1f)
        {
            var rt = Rect(parent, "Pattern");
            Stretch(rt);
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = texture;
            raw.color = new Color(1, 1, 1, opacity);
            raw.raycastTarget = false;
            rt.gameObject.AddComponent<TiledPattern>().tile = tile;
            return raw;
        }

        // ------------------------------------------------------------ type

        public static Text Label(Transform parent, string text, int size, TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Theme.Font;
            t.text = text;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color ?? Theme.Ink;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.lineSpacing = 0.95f;
            return t;
        }

        // Uppercase heavy heading with tight leading.
        public static Text Heading(Transform parent, string text, int size, TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
        {
            var t = Label(parent, text.ToUpperInvariant(), size, anchor, color);
            t.lineSpacing = 0.85f;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // Shortens long names (e.g. imported file names) to fit a box, cutting at a word where possible.
        public static string Shorten(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
            int cut = text.LastIndexOf(' ', maxChars - 3);
            if (cut < maxChars / 2) cut = maxChars - 3;
            return text.Substring(0, cut).TrimEnd(' ', '-', '_', ',') + "...";
        }

        // Makes text wrap and shrink (down to minSize) so it always stays inside its rect.
        public static Text FitInside(Text t, int minSize)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = minSize;
            t.resizeTextMaxSize = t.fontSize;
            t.lineSpacing = 0.9f;
            return t;
        }

        // Hollow outlined display text: fill matches the background, black stroke around it.
        public static Text Outlined(Transform parent, string text, int size, Color background, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var t = Heading(parent, text, size, anchor, background);
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = Theme.Ink;
            o.effectDistance = new Vector2(3, -3);
            return t;
        }

        // ------------------------------------------------------------ buttons

        // Bordered button with a hard shadow that presses down onto its shadow when touched.
        public static Button Button(Transform parent, string label, Action onClick, bool world, Color? color = null, int fontSize = 30)
        {
            var root = Rect(parent, label + " Button");
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;                       // the root catches touches; the face moves
            var button = root.gameObject.AddComponent<Button>();

            var face = Card(root, color ?? Theme.Red, Theme.ShadowSm + new Vector2(2, -2), "Face");
            var faceImage = face.GetComponent<Image>();
            faceImage.raycastTarget = false;
            button.targetGraphic = faceImage;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.93f, 0.93f, 0.93f);
            colors.pressedColor = new Color(0.86f, 0.86f, 0.86f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.72f, 0.72f, 0.72f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            var text = Heading(face, label, fontSize, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 8);

            root.gameObject.AddComponent<PressEffect>().Init(face, face.GetComponent<Shadow>());
            if (world) PointerTarget.ForButton(button);
            return button;
        }

        // Chunky bordered slider: white track, coloured fill, square yellow handle.
        public static Slider Slider(Transform parent, float value, Color fill, Action<float> onChange)
        {
            var root = Rect(parent, "Slider");
            var slider = root.gameObject.AddComponent<UnityEngine.UI.Slider>();

            var track = Card(root, Theme.White, Theme.ShadowSm, "Track");
            track.anchorMin = new Vector2(0, 0.3f);
            track.anchorMax = new Vector2(1, 0.7f);
            track.offsetMin = track.offsetMax = Vector2.zero;

            var fillArea = Rect(root, "Fill Area");
            fillArea.anchorMin = new Vector2(0, 0.3f);
            fillArea.anchorMax = new Vector2(1, 0.7f);
            fillArea.offsetMin = new Vector2(Theme.Border, Theme.Border);
            fillArea.offsetMax = new Vector2(-Theme.Border, -Theme.Border);
            var fillRt = Panel(fillArea, fill, "Fill");
            fillRt.GetComponent<Image>().raycastTarget = false;

            var handleArea = Rect(root, "Handle Area");
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(30, 0);
            handleArea.offsetMax = new Vector2(-30, 0);
            var handle = Card(handleArea, Theme.Yellow, Theme.ShadowSm, "Handle");
            handle.sizeDelta = new Vector2(60, 0);
            handle.anchorMin = new Vector2(0, 0);
            handle.anchorMax = new Vector2(0, 1);

            slider.fillRect = fillRt;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = value;
            if (onChange != null) slider.onValueChanged.AddListener(v => onChange(v));
            return slider;
        }

        public static void SetButtonLabel(Button button, string label) =>
            button.GetComponentInChildren<Text>().text = label.ToUpperInvariant();

        // ------------------------------------------------------------ stickers & decoration

        // Small bordered label box, optionally tilted like a sticker.
        public static RectTransform Badge(Transform parent, string text, Color fill, int fontSize, float rotation = 0, Color? textColor = null)
        {
            var card = Card(parent, fill, Theme.ShadowSm, "Badge " + text);
            var t = Heading(card, text, fontSize, TextAnchor.MiddleCenter, textColor);
            Stretch(t.rectTransform, 6);
            card.localRotation = Quaternion.Euler(0, 0, rotation);
            return card;
        }

        public static void Tilt(RectTransform rt, float degrees) => rt.localRotation = Quaternion.Euler(0, 0, degrees);

        public static Image Circle(Transform parent, int diameter, Color fill, Vector2 anchor, Vector2 offset, bool shadow = true)
        {
            var rt = Rect(parent, "Circle");
            Place(rt, anchor, new Vector2(diameter, diameter), offset);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Theme.Circle(diameter);
            img.color = fill;
            img.raycastTarget = false;
            if (shadow) HardShadow(img, Theme.ShadowSm);
            return img;
        }

        public static Image Star(Transform parent, int size, Color fill, Vector2 anchor, Vector2 offset, bool spin = true)
        {
            var rt = Rect(parent, "Star");
            Place(rt, anchor, new Vector2(size, size), offset);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Theme.Star(size);
            img.color = fill;
            img.raycastTarget = false;
            HardShadow(img, Theme.ShadowSm);
            if (spin) rt.gameObject.AddComponent<SpinSlow>();
            return img;
        }

        // Black band with repeating uppercase text scrolling sideways.
        public static RectTransform Marquee(Transform parent, string text, float height, Color background, Color textColor)
        {
            var band = Rect(parent, "Marquee");
            band.anchorMin = new Vector2(0, 1);
            band.anchorMax = new Vector2(1, 1);
            band.pivot = new Vector2(0.5f, 1);
            band.sizeDelta = new Vector2(0, height);
            band.anchoredPosition = Vector2.zero;
            band.gameObject.AddComponent<Image>().color = background;
            band.gameObject.AddComponent<RectMask2D>();

            var line = Rect(band, "Line");
            line.anchorMin = new Vector2(0, 0);
            line.anchorMax = new Vector2(0, 1);
            line.pivot = new Vector2(0, 0.5f);
            var t = line.gameObject.AddComponent<Text>();
            t.font = Theme.Font;
            t.fontSize = Mathf.RoundToInt(height * 0.48f);
            t.color = textColor;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            string unit = text.ToUpperInvariant();
            t.text = string.Concat(System.Linq.Enumerable.Repeat(unit, 8));
            line.gameObject.AddComponent<MarqueeScroll>().Init(t, unit);

            var rule = Rect(band, "Rule");
            rule.anchorMin = new Vector2(0, 0);
            rule.anchorMax = new Vector2(1, 0);
            rule.pivot = new Vector2(0.5f, 0);
            rule.sizeDelta = new Vector2(0, Theme.Border);
            rule.gameObject.AddComponent<Image>().color = Theme.Ink;
            return band;
        }
    }

    // ---------------------------------------------------------------- behaviours

    // Mechanical push: the face slides onto its shadow while held.
    public class PressEffect : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        RectTransform face;
        Shadow shadow;
        Vector2 depth;
        Button button;

        public void Init(RectTransform face, Shadow shadow)
        {
            this.face = face;
            this.shadow = shadow;
            depth = shadow != null ? shadow.effectDistance : Theme.ShadowSm;
            button = GetComponent<Button>();
        }

        public void OnPointerDown(PointerEventData e) { if (button == null || button.interactable) Press(true); }
        public void OnPointerUp(PointerEventData e) => Press(false);
        public void OnPointerExit(PointerEventData e) => Press(false);

        // Quick press-and-release, used when the Quest laser clicks.
        public void Pulse()
        {
            Press(true);
            CancelInvoke(nameof(Release));
            Invoke(nameof(Release), 0.1f);
        }

        void Release() => Press(false);

        void Press(bool down)
        {
            if (face == null) return;
            var offset = down ? depth : Vector2.zero;
            face.offsetMin = offset;
            face.offsetMax = offset;
            if (shadow != null) shadow.effectDistance = down ? Vector2.zero : depth;
        }
    }

    // Press-and-hold repeat for a button: a quick tap gives one small step, holding glides
    // with speed ramping up (slow for fine adjustment, fast for big moves).
    public class HoldRepeat : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action<float> onStep;       // receives the amount to apply this frame
        public float tapStep = 0.02f;
        public float minSpeed = 0.1f;      // units per second when the hold starts
        public float maxSpeed = 1.0f;      // reached after rampSeconds
        public float rampSeconds = 1.5f;

        const float HoldDelay = 0.25f;
        float pressedAt = -1f;
        bool repeating;

        public static HoldRepeat Attach(Button button, Action<float> onStep, float tapStep, float minSpeed, float maxSpeed)
        {
            var h = button.gameObject.AddComponent<HoldRepeat>();
            h.onStep = onStep;
            h.tapStep = tapStep;
            h.minSpeed = minSpeed;
            h.maxSpeed = maxSpeed;
            return h;
        }

        public void OnPointerDown(PointerEventData e)
        {
            pressedAt = Time.unscaledTime;
            repeating = false;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (pressedAt >= 0 && !repeating) onStep?.Invoke(tapStep);
            pressedAt = -1f;
        }

        public void OnPointerExit(PointerEventData e) => pressedAt = -1f;

        void Update()
        {
            if (pressedAt < 0) return;
            float held = Time.unscaledTime - pressedAt;
            if (held < HoldDelay) return;
            repeating = true;
            float t = Mathf.Clamp01((held - HoldDelay) / rampSeconds);
            onStep?.Invoke(Mathf.Lerp(minSpeed, maxSpeed, t * t) * Time.unscaledDeltaTime);
        }

        void OnDisable() => pressedAt = -1f;
    }

    public class TiledPattern : MonoBehaviour
    {
        public float tile = 20f;
        RawImage raw;
        RectTransform rt;

        void Awake() { raw = GetComponent<RawImage>(); rt = (RectTransform)transform; }

        void LateUpdate()
        {
            var size = rt.rect.size;
            raw.uvRect = new Rect(0, 0, Mathf.Max(1, size.x / tile), Mathf.Max(1, size.y / tile));
        }
    }

    public class SpinSlow : MonoBehaviour
    {
        public float secondsPerTurn = 10f;
        void Update() => transform.Rotate(0, 0, -360f / secondsPerTurn * Time.unscaledDeltaTime);
    }

    public class MarqueeScroll : MonoBehaviour
    {
        const float Speed = 70f; // canvas units per second
        Text text;
        float unitWidth;
        RectTransform rt;

        public void Init(Text t, string unit)
        {
            text = t;
            rt = (RectTransform)transform;
            var settings = t.GetGenerationSettings(Vector2.zero);
            unitWidth = t.cachedTextGeneratorForLayout.GetPreferredWidth(unit, settings) / t.pixelsPerUnit;
            rt.sizeDelta = new Vector2(t.cachedTextGeneratorForLayout.GetPreferredWidth(t.text, settings) / t.pixelsPerUnit, 0);
        }

        void Update()
        {
            if (unitWidth <= 0) return;
            float x = rt.anchoredPosition.x - Speed * Time.unscaledDeltaTime;
            if (x < -unitWidth) x += unitWidth;
            rt.anchoredPosition = new Vector2(x, 0);
        }
    }
}
