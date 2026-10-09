using System;
using UnityEngine;
using UnityEngine.UI;

namespace VRLandscape
{
    // Phone overlay during a walkthrough or 360 tour: sticker title, chunky bordered buttons,
    // a bordered joystick, and an Info card showing the numbers needed for park.json.
    public class PhoneHud : MonoBehaviour
    {
        PlayerRig rig;
        VirtualJoystick joystick, lookStick;
        Text title, info;
        Button gyroButton;
        Func<float> recenterYaw;

        public static PhoneHud Create(PlayerRig rig, bool walking, string titleText, Action onBack, Func<float> recenterYaw, Action onSync = null)
        {
            var canvas = UIFactory.CreateCanvas("Phone HUD", world: false);
            var hud = canvas.gameObject.AddComponent<PhoneHud>();
            hud.rig = rig;
            hud.recenterYaw = recenterYaw;
            var root = canvas.transform;

            var look = LookPad.Create(root);
            look.OnLook = d => rig.Phone.Look(d);
            look.OnTap = hud.TapWorld;

            if (walking)
            {
                hud.joystick = VirtualJoystick.Create(root, 260, false, Theme.Red, "Move");
                hud.lookStick = VirtualJoystick.Create(root, 260, true, Theme.Violet, "Look");
            }

            var back = UIFactory.Button(root, "← Parks", onBack, false, Theme.White, 28);
            UIFactory.Place((RectTransform)back.transform, new Vector2(0, 1), new Vector2(210, 84), new Vector2(32, -32));
            if (onSync != null)
            {
                var sync = UIFactory.Button(root, "Sync", onSync, false, Theme.Violet, 26);
                UIFactory.Place((RectTransform)sync.transform, new Vector2(0, 1), new Vector2(140, 84), new Vector2(262, -32));
            }

            var titleCard = UIFactory.Card(root, Theme.Yellow, Theme.ShadowMd, "Title");
            UIFactory.Place(titleCard, new Vector2(0.5f, 1), new Vector2(640, 92), new Vector2(0, -32));
            UIFactory.Tilt(titleCard, -1f);
            titleCard.GetComponent<Image>().raycastTarget = false;
            hud.title = UIFactory.Heading(titleCard, "", 32, TextAnchor.MiddleCenter);
            UIFactory.Stretch(hud.title.rectTransform, 12);
            UIFactory.FitInside(hud.title, 18);
            hud.SetTitle(titleText);

            float x = -32;
            var infoButton = UIFactory.Button(root, "Info", null, false, Theme.White, 26);
            UIFactory.Place((RectTransform)infoButton.transform, new Vector2(1, 1), new Vector2(130, 84), new Vector2(x, -32));
            x -= 150;
            var recenter = UIFactory.Button(root, "Recenter", hud.Recenter, false, Theme.White, 26);
            UIFactory.Place((RectTransform)recenter.transform, new Vector2(1, 1), new Vector2(200, 84), new Vector2(x, -32));
            x -= 220;
            if (rig.Phone.GyroAvailable)
            {
                hud.gyroButton = UIFactory.Button(root, "", hud.ToggleGyro, false, Theme.Violet, 26);
                UIFactory.Place((RectTransform)hud.gyroButton.transform, new Vector2(1, 1), new Vector2(200, 84), new Vector2(x, -32));
                hud.RefreshGyroLabel();
            }

            var infoCard = UIFactory.Card(root, Theme.White, Theme.ShadowMd, "Info");
            UIFactory.Place(infoCard, new Vector2(0, 1), new Vector2(560, 170), new Vector2(32, -140));
            infoCard.GetComponent<Image>().raycastTarget = false;
            hud.info = UIFactory.Label(infoCard, "", 26, TextAnchor.MiddleLeft);
            UIFactory.Stretch(hud.info.rectTransform, 22);
            infoCard.gameObject.SetActive(false);
            infoButton.onClick.AddListener(() => infoCard.gameObject.SetActive(!infoCard.gameObject.activeSelf));

            var hint = UIFactory.Badge(root, walking
                    ? "Left stick: walk  •  Right stick / drag: look  •  Tap 360° marker"
                    : "Look around  •  Drag: turn  •  Tap a marker to move",
                Theme.Ink, 22, 0, Theme.Cream);
            UIFactory.Place(hint, new Vector2(0.5f, 0), new Vector2(900, 56), new Vector2(0, 32));
            hint.GetComponent<Image>().raycastTarget = false;
            return hud;
        }

        // Two lines at most; long imported file names are shortened with "...".
        public void SetTitle(string text) => title.text = UIFactory.Shorten(text, 64).ToUpperInvariant();

        // "Park name" over "viewpoint name" for the 360 tour, each shortened on its own.
        // (Imported single panoramas use the file name for both, so it's shown once.)
        public static string TwoPart(string park, string view) =>
            string.IsNullOrEmpty(view) || view == park
                ? UIFactory.Shorten(park, 60)
                : UIFactory.Shorten(park, 30) + "\n" + UIFactory.Shorten(view, 30);

        void Update()
        {
            if (joystick != null) rig.Phone.JoystickInput = joystick.Value;
            if (lookStick != null)
            {
                // PUBG-style camera stick only when the gyro isn't steering the view.
                bool show = !rig.Phone.GyroEnabled;
                if (lookStick.gameObject.activeSelf != show) lookStick.gameObject.SetActive(show);
                rig.Phone.LookStickInput = show ? lookStick.Value : Vector2.zero;
            }
            if (info.gameObject.activeInHierarchy)
            {
                var cam = rig.Cam.transform;
                float pitch = -Mathf.DeltaAngle(0, cam.eulerAngles.x);
                var p = rig.transform.position;
                info.text = $"YAW {cam.eulerAngles.y:0}°   PITCH {pitch:0}°\n" +
                            $"POSITION [{p.x:0.0}, {p.y:0.0}, {p.z:0.0}]\n" +
                            "Copy these into park.json for hotspots / spawn";
            }
        }

        void OnDestroy()
        {
            if (rig != null && rig.Phone != null)
            {
                rig.Phone.JoystickInput = Vector2.zero;
                rig.Phone.LookStickInput = Vector2.zero;
            }
        }

        void TapWorld(Vector2 screenPos)
        {
            var ray = rig.Cam.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out var hit, 200f, ~0, QueryTriggerInteraction.Collide))
                hit.collider.GetComponentInParent<PointerTarget>()?.Activate();
        }

        void ToggleGyro()
        {
            rig.Phone.SetGyro(!rig.Phone.GyroEnabled);
            RefreshGyroLabel();
        }

        void RefreshGyroLabel() =>
            UIFactory.SetButtonLabel(gyroButton, rig.Phone.GyroEnabled ? "Gyro: On" : "Gyro: Off");

        void Recenter() => rig.Phone.SetYaw(recenterYaw());
    }
}
