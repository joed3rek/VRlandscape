using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRLandscape
{
    // On-screen thumb joystick: bottom-left walks, bottom-right turns the camera (when the gyro is off).
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Vector2 Value { get; private set; }

        RectTransform area, knob;
        float radius;

        public static VirtualJoystick Create(Transform parent, float size, bool rightSide = false, Color? knobColor = null, string label = null)
        {
            // Cream disc with a black centre dot, coloured knob with a hard shadow, small label sticker.
            int d = Mathf.RoundToInt(size);
            var corner = rightSide ? new Vector2(1, 0) : new Vector2(0, 0);
            var baseImg = UIFactory.Circle(parent, d, Theme.Cream, corner, new Vector2(rightSide ? -60 : 60, 60));
            baseImg.raycastTarget = true;
            baseImg.name = "Joystick";
            var baseRt = baseImg.rectTransform;
            UIFactory.Circle(baseRt, Mathf.RoundToInt(size * 0.18f), Theme.Ink, new Vector2(0.5f, 0.5f), Vector2.zero, shadow: false);
            var knobImg = UIFactory.Circle(baseRt, Mathf.RoundToInt(size * 0.42f), knobColor ?? Theme.Red, new Vector2(0.5f, 0.5f), Vector2.zero);
            knobImg.name = "Knob";
            var knobRt = knobImg.rectTransform;
            if (label != null)
            {
                var tag = UIFactory.Badge(baseRt, label, Theme.Ink, 20, rightSide ? 4f : -4f, Theme.Cream);
                UIFactory.Place(tag, new Vector2(0.5f, 1), new Vector2(110, 40), new Vector2(0, 26));
                tag.GetComponent<Image>().raycastTarget = false;
            }

            var js = baseRt.gameObject.AddComponent<VirtualJoystick>();
            js.area = baseRt;
            js.knob = knobRt;
            js.radius = size * 0.5f;
            return js;
        }

        public void OnPointerDown(PointerEventData e) => OnDrag(e);

        public void OnDrag(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, e.position, e.pressEventCamera, out var local)) return;
            local -= area.rect.center;
            var clamped = Vector2.ClampMagnitude(local, radius);
            knob.anchoredPosition = clamped;
            Value = clamped / radius;
        }

        public void OnPointerUp(PointerEventData e)
        {
            knob.anchoredPosition = Vector2.zero;
            Value = Vector2.zero;
        }
    }

    // Full-screen transparent layer for AR: one-finger drag, two-finger pinch and twist, tap.
    public class GestureLayer : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IPointerClickHandler
    {
        public Action<Vector2, Vector2> OnPan;      // previous and current screen position
        public Action<float> OnPinch;               // scale factor since last frame
        public Action<float> OnTwist;               // degrees since last frame, clockwise positive
        public Action<Vector2> OnTap;

        readonly System.Collections.Generic.Dictionary<int, Vector2> pointers = new System.Collections.Generic.Dictionary<int, Vector2>();

        public static GestureLayer Create(Transform parent)
        {
            var rt = UIFactory.Panel(parent, new Color(0, 0, 0, 0), "Gesture Layer");
            return rt.gameObject.AddComponent<GestureLayer>();
        }

        public void OnPointerDown(PointerEventData e) => pointers[e.pointerId] = e.position;
        public void OnPointerUp(PointerEventData e) => pointers.Remove(e.pointerId);

        public void OnPointerClick(PointerEventData e)
        {
            if (!e.dragging && pointers.Count == 0) OnTap?.Invoke(e.position);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!pointers.TryGetValue(e.pointerId, out var previous)) previous = e.position - e.delta;

            if (pointers.Count >= 2)
            {
                Vector2 other = Vector2.zero;
                foreach (var kv in pointers)
                    if (kv.Key != e.pointerId) { other = kv.Value; break; }

                var before = previous - other;
                var after = e.position - other;
                if (before.magnitude > 10f && after.magnitude > 10f)
                {
                    OnPinch?.Invoke(after.magnitude / before.magnitude);
                    OnTwist?.Invoke(-Vector2.SignedAngle(before, after));
                }
            }
            else
            {
                OnPan?.Invoke(previous, e.position);
            }
            pointers[e.pointerId] = e.position;
        }
    }

    // Full-screen transparent layer behind the HUD: drag to look, tap to select things in the world.
    public class LookPad : MonoBehaviour, IDragHandler, IPointerClickHandler
    {
        public Action<Vector2> OnLook;
        public Action<Vector2> OnTap;

        public static LookPad Create(Transform parent)
        {
            var rt = UIFactory.Panel(parent, new Color(0, 0, 0, 0), "Look Pad");
            return rt.gameObject.AddComponent<LookPad>();
        }

        public void OnDrag(PointerEventData e) => OnLook?.Invoke(e.delta);

        public void OnPointerClick(PointerEventData e)
        {
            if (!e.dragging) OnTap?.Invoke(e.position);
        }
    }
}
