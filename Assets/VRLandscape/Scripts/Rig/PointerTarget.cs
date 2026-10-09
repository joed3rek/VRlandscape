using System;
using UnityEngine;
using UnityEngine.UI;

namespace VRLandscape
{
    // Anything selectable by the Quest laser or a phone tap: hotspots, 360 markers, world-space menu buttons.
    public class PointerTarget : MonoBehaviour
    {
        public Action onActivate;
        public Action<bool> onHover;

        RectTransform rect;
        BoxCollider box;

        public void SetHover(bool hovered) => onHover?.Invoke(hovered);
        public void Activate() => onActivate?.Invoke();

        // Makes a world-space UI button clickable by the laser.
        public static PointerTarget ForButton(Button button)
        {
            var target = button.gameObject.AddComponent<PointerTarget>();
            target.rect = (RectTransform)button.transform;
            target.box = button.gameObject.AddComponent<BoxCollider>();
            target.box.isTrigger = true;
            var graphic = button.targetGraphic;
            var normal = graphic != null ? graphic.color : Color.white;
            var hover = normal * 0.88f;
            hover.a = normal.a;
            var press = button.GetComponent<PressEffect>();
            target.onActivate = () =>
            {
                if (!button.interactable) return;
                if (press != null) press.Pulse();
                button.onClick.Invoke();
            };
            target.onHover = h => { if (graphic != null) graphic.color = h ? hover : normal; };
            return target;
        }

        void LateUpdate()
        {
            if (rect == null || box == null) return;
            var r = rect.rect;
            box.center = new Vector3(r.center.x, r.center.y, 0);
            box.size = new Vector3(r.width, r.height, 1f);
        }
    }
}
