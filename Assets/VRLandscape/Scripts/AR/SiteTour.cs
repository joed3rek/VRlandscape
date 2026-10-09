using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VRLandscape
{
    // On-site mode ("AR render window"): visitors walk the real site; each 360 render has a GPS spot.
    //  - Spot list: distance + direction arrow to every render spot, a big guide to the nearest one.
    //  - Arriving within ~8-10 m opens that render automatically (with a buzz), aligned to the real
    //    directions by the compass, over the live camera. A slider fades between NOW and PROPOSED.
    //  - Designers set spots on site with SET HERE (stand at the render camera spot, face the view centre)
    //    and fine-tune with ALIGN (drag) + SAVE. Spots are remembered on the device (GeoStore).
    public class SiteTour : MonoBehaviour
    {
        class Spot
        {
            public PanoramaDef def;
            public GeoDef geo;
            public float distance = -1f;
            public float bearing;
        }

        class Row
        {
            public Spot spot;
            public Text distance;
            public RectTransform arrow;
        }

        const float MinTriggerRadius = 8f;
        const float MaxTriggerRadius = 25f;
        const float AlignDegreesPerPixel = 0.08f;

        ParkDef park;
        PlayerRig rig;
        Action onExit;

        GeoLocation gps;
        Camera cam;
        bool hasCamera;
        readonly List<Spot> spots = new List<Spot>();
        readonly List<Row> rows = new List<Row>();
        Spot openSpot, suppressed;

        // List screen
        RectTransform listScreen, guideArrow, listContent;
        Text guideTitle, guideDetail, gpsText;
        Image gpsBadge;

        // Render window
        RectTransform windowScreen;
        Text windowTitle, alignHint;
        Button alignButton, saveButton;
        UnityEngine.UI.Slider slider;
        Transform sphere;
        Material panoMaterial;
        float northYaw, alignOffset;
        bool aligning;
        readonly Dictionary<string, Texture> loaded = new Dictionary<string, Texture>();

        public static SiteTour Create(Transform parent, ParkDef park, PlayerRig rig, Action onExit)
        {
            var go = new GameObject("Site Tour");
            go.transform.SetParent(parent, false);
            var tour = go.AddComponent<SiteTour>();
            tour.park = park;
            tour.rig = rig;
            tour.onExit = onExit;
            return tour;
        }

        IEnumerator Start()
        {
            BuildUI();
            SetGuide("Starting camera...", "");

            ARRigFactory.Rig arRig = null;
            yield return ARRigFactory.Start(transform, null, s => SetGuide(s, ""), r => arRig = r);
            if (arRig != null)
            {
                hasCamera = true;
                cam = arRig.camera;
                rig.gameObject.SetActive(false);
            }
            else
            {
                cam = rig.Cam;
                rig.MovementEnabled = false;
                rig.Teleport(Vector3.zero, 0);
            }

            if (park.isDemo && park.runtimeTextures == null)
            {
                SetGuide("Preparing renders...", "");
                yield return null;
                DemoPark.RenderPanoramas(park, transform);
            }

            gps = GeoLocation.Ensure();
            foreach (var p in park.panoramas)
                spots.Add(new Spot { def = p, geo = GeoStore.Get(park, p) });
            BuildRows();
            BuildSphere();
            ShowList();
            if (!hasCamera) Toast("No AR camera on this device: renders show without the live view.");
        }

        // ------------------------------------------------------------ update

        void Update()
        {
            if (gps == null) return;
            UpdateGpsBadge();
            if (openSpot != null) { UpdateWindow(); return; }
            UpdateDistances();
            UpdateList();
            AutoOpen();
        }

        void UpdateGpsBadge()
        {
            bool ok = gps.Status == GeoLocation.State.Running;
            gpsText.text = gps.Status == GeoLocation.State.Starting ? "GPS..." : ok ? $"GPS ±{Mathf.RoundToInt(gps.Accuracy)} m" : "NO GPS";
            gpsBadge.color = ok ? (gps.Accuracy <= 12 ? Theme.Violet : Theme.Yellow) : Theme.Red;
        }

        void UpdateDistances()
        {
            bool ok = gps.Status == GeoLocation.State.Running;
            foreach (var s in spots)
            {
                if (!ok || s.geo == null) { s.distance = -1f; continue; }
                s.distance = GeoLocation.Distance(gps.Latitude, gps.Longitude, s.geo.lat, s.geo.lon);
                s.bearing = GeoLocation.Bearing(gps.Latitude, gps.Longitude, s.geo.lat, s.geo.lon);
            }
        }

        Spot Nearest()
        {
            Spot best = null;
            foreach (var s in spots)
                if (s.distance >= 0 && (best == null || s.distance < best.distance)) best = s;
            return best;
        }

        float TriggerRadius => Mathf.Clamp(gps.Accuracy * 1.2f, MinTriggerRadius, MaxTriggerRadius);

        void UpdateList()
        {
            foreach (var r in rows)
            {
                r.distance.text = r.spot.geo == null ? "NOT SET" : r.spot.distance < 0 ? "—" : FormatDistance(r.spot.distance);
                bool point = r.spot.distance >= 0 && gps.HasHeading;
                r.arrow.gameObject.SetActive(point);
                if (point) r.arrow.localRotation = Quaternion.Euler(0, 0, -(r.spot.bearing - gps.Heading));
            }

            var nearest = Nearest();
            if (gps.Status == GeoLocation.State.Failed) { SetGuide("GPS unavailable", gps.Error); guideArrow.gameObject.SetActive(false); return; }
            if (gps.Status == GeoLocation.State.Starting) { SetGuide("Finding your location...", "Stand outdoors with a clear view of the sky"); guideArrow.gameObject.SetActive(false); return; }
            if (nearest == null)
            {
                SetGuide("No render spots set yet", "Stand where a render was taken, face its view and tap SET HERE");
                guideArrow.gameObject.SetActive(false);
                return;
            }
            string name = UIFactory.Shorten(nearest.def.name ?? nearest.def.id, 28);
            string where = gps.HasHeading ? FormatDistance(nearest.distance) + " that way" : FormatDistance(nearest.distance) + " " + Compass(nearest.bearing);
            SetGuide("Walk to " + name, where + "  •  opens automatically when you arrive");
            guideArrow.gameObject.SetActive(gps.HasHeading);
            if (gps.HasHeading) guideArrow.localRotation = Quaternion.Euler(0, 0, -(nearest.bearing - gps.Heading));
        }

        void AutoOpen()
        {
            var nearest = Nearest();
            if (nearest == null) return;
            if (suppressed != null && (suppressed.distance < 0 || suppressed.distance > TriggerRadius * 1.5f)) suppressed = null;
            if (nearest.distance <= TriggerRadius && nearest != suppressed)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Handheld.Vibrate();
#endif
                Open(nearest);
            }
        }

        // ------------------------------------------------------------ render window

        async void Open(Spot spot)
        {
            openSpot = spot;
            suppressed = spot;
            alignOffset = 0f;
            SetAligning(false);
            listScreen.gameObject.SetActive(false);
            windowScreen.gameObject.SetActive(true);
            windowTitle.text = UIFactory.Shorten(spot.def.name ?? spot.def.id, 34).ToUpperInvariant();
            slider.value = 1f;

            // World yaw of north = where the camera points minus the compass heading it reads.
            if (gps.HasHeading) northYaw = cam.transform.eulerAngles.y - gps.Heading;
            else
            {
                northYaw = cam.transform.eulerAngles.y;
                Toast("No compass reading: tap ALIGN and drag until the render lines up.");
            }
            if (spot.geo == null) Toast("This spot isn't set yet: tap ALIGN, line it up, then SAVE to remember it here.");

            sphere.gameObject.SetActive(false);
            var tex = await LoadTexture(spot.def);
            if (this == null || openSpot != spot) return;
            if (tex is RenderTexture rt && rt.dimension == UnityEngine.Rendering.TextureDimension.Cube)
            {
                panoMaterial.SetTexture("_Cube", rt);
                panoMaterial.SetFloat("_UseCube", 1);
            }
            else
            {
                panoMaterial.SetTexture("_MainTex", tex);
                panoMaterial.SetFloat("_UseCube", 0);
                float aspect = tex.height > 0 ? (float)tex.width / tex.height : 2f;
                panoMaterial.SetFloat("_VCover", Mathf.Min(180f, 360f / Mathf.Max(aspect, 0.01f)));
            }
            sphere.gameObject.SetActive(true);
        }

        void UpdateWindow()
        {
            sphere.position = cam.transform.position;
            float heading = openSpot.geo != null ? openSpot.geo.heading : 0f;
            // The image centre faces compass `heading`; in world space that is northYaw + heading.
            panoMaterial.SetFloat("_Yaw", northYaw + heading + alignOffset);
        }

        void CloseWindow()
        {
            openSpot = null;
            SetAligning(false);
            sphere.gameObject.SetActive(false);
            windowScreen.gameObject.SetActive(false);
            ShowList();
        }

        async System.Threading.Tasks.Task<Texture> LoadTexture(PanoramaDef def)
        {
            if (park.runtimeTextures != null && park.runtimeTextures.TryGetValue(def.id, out var runtime)) return runtime;
            if (loaded.TryGetValue(def.id, out var cached)) return cached;
            var tex = await FileLoader.ReadTexture(FileLoader.Combine(park.baseDir, def.image));
            if (tex == null) return Texture2D.grayTexture;
            loaded[def.id] = tex;
            return tex;
        }

        // ------------------------------------------------------------ setting spots

        void SetHere(Spot spot)
        {
            if (gps.Status != GeoLocation.State.Running) { Toast("No GPS fix yet. Wait a moment outdoors."); return; }
            var geo = new GeoDef { lat = gps.Latitude, lon = gps.Longitude, heading = gps.HasHeading ? gps.Heading : 0f };
            spot.geo = geo;
            GeoStore.Set(park, spot.def, geo);
            string accuracy = gps.Accuracy > 15 ? $" GPS is only ±{Mathf.RoundToInt(gps.Accuracy)} m right now; set it again for a better fix." : "";
            Toast($"Saved {spot.def.name ?? spot.def.id} here, facing {Mathf.RoundToInt(geo.heading)}° ({Compass(geo.heading)}).{accuracy}");
            suppressed = spot; // don't pop the render open while you're standing on it
        }

        void SaveAlignment()
        {
            var spot = openSpot;
            if (spot == null) return;
            var geo = spot.geo ?? new GeoDef();
            if (!geo.IsSet && gps.Status == GeoLocation.State.Running) { geo.lat = gps.Latitude; geo.lon = gps.Longitude; }
            geo.heading = Mathf.Repeat(geo.heading + alignOffset, 360f);
            spot.geo = geo;
            alignOffset = 0f;
            GeoStore.Set(park, spot.def, geo);
            SetAligning(false);
            Toast("Alignment saved for this spot.");
        }

        void SetAligning(bool on)
        {
            aligning = on;
            if (alignButton == null) return;
            UIFactory.SetButtonLabel(alignButton, on ? "Cancel" : "Align");
            saveButton.gameObject.SetActive(on);
            alignHint.transform.parent.gameObject.SetActive(on);
            if (!on && openSpot != null) alignOffset = 0f;
        }

        void Drag(Vector2 from, Vector2 to)
        {
            if (openSpot == null) return;
            if (aligning) alignOffset += (to.x - from.x) * AlignDegreesPerPixel;
            else if (!hasCamera) rig.Phone.Look(to - from);
        }

        // ------------------------------------------------------------ UI

        void BuildUI()
        {
            var canvas = UIFactory.CreateCanvas("Site HUD", world: false);
            canvas.transform.SetParent(transform, false);
            var root = canvas.transform;

            var gestures = GestureLayer.Create(root);
            gestures.OnPan = Drag;

            // ---- list screen
            listScreen = UIFactory.Rect(root, "List");
            UIFactory.Stretch(listScreen);

            var back = UIFactory.Button(listScreen, "← Parks", () => onExit?.Invoke(), false, Theme.White, 28);
            UIFactory.Place((RectTransform)back.transform, new Vector2(0, 1), new Vector2(210, 84), new Vector2(32, -32));

            var gpsCard = UIFactory.Badge(listScreen, "GPS...", Theme.Yellow, 24, 2f);
            UIFactory.Place(gpsCard, new Vector2(1, 1), new Vector2(200, 60), new Vector2(-40, -40));
            gpsBadge = gpsCard.GetComponent<Image>();
            gpsText = gpsCard.GetComponentInChildren<Text>();

            // Guide card: big arrow + where to walk
            var guide = UIFactory.Card(listScreen, Theme.White, Theme.ShadowMd, "Guide");
            UIFactory.Place(guide, new Vector2(0.5f, 1), new Vector2(980, 170), new Vector2(0, -32));
            UIFactory.Tilt(guide, -0.6f);
            guide.GetComponent<Image>().raycastTarget = false;
            var arrowDisc = UIFactory.Circle(guide, 130, Theme.Yellow, new Vector2(0, 0.5f), new Vector2(22, 0));
            guideArrow = arrowDisc.rectTransform;
            var arrowText = UIFactory.Heading(guideArrow, "↑", 96, TextAnchor.MiddleCenter);
            UIFactory.Stretch(arrowText.rectTransform);
            guideTitle = UIFactory.Heading(guide, "", 40, TextAnchor.LowerLeft);
            guideTitle.rectTransform.anchorMin = new Vector2(0, 0.5f);
            guideTitle.rectTransform.anchorMax = new Vector2(1, 1);
            guideTitle.rectTransform.offsetMin = new Vector2(180, 0);
            guideTitle.rectTransform.offsetMax = new Vector2(-24, -14);
            UIFactory.FitInside(guideTitle, 24);
            guideDetail = UIFactory.Label(guide, "", 24, TextAnchor.UpperLeft);
            guideDetail.rectTransform.anchorMin = new Vector2(0, 0);
            guideDetail.rectTransform.anchorMax = new Vector2(1, 0.5f);
            guideDetail.rectTransform.offsetMin = new Vector2(180, 14);
            guideDetail.rectTransform.offsetMax = new Vector2(-24, -4);
            UIFactory.FitInside(guideDetail, 16);

            // Spot list (scrolls)
            var list = UIFactory.Card(listScreen, Theme.Cream, Theme.ShadowLg, "Spots");
            list.anchorMin = new Vector2(0.5f, 0);
            list.anchorMax = new Vector2(0.5f, 0);
            list.pivot = new Vector2(0.5f, 0);
            list.sizeDelta = new Vector2(1180, 560);
            list.anchoredPosition = new Vector2(0, 40);
            var label = UIFactory.Badge(list, "Render spots", Theme.Ink, 22, -2f, Theme.Yellow);
            UIFactory.Place(label, new Vector2(0, 1), new Vector2(240, 50), new Vector2(20, 26));

            var viewport = UIFactory.Rect(list, "Viewport");
            UIFactory.Stretch(viewport, 20);
            viewport.offsetMax = new Vector2(-20, -36);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            listContent = UIFactory.Rect(viewport, "Content");
            listContent.anchorMin = new Vector2(0, 1);
            listContent.anchorMax = new Vector2(1, 1);
            listContent.pivot = new Vector2(0.5f, 1);
            listContent.offsetMin = listContent.offsetMax = Vector2.zero;
            var vl = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 14;
            vl.padding = new RectOffset(4, 12, 8, 12);
            vl.childControlWidth = vl.childControlHeight = true;
            vl.childForceExpandHeight = false;
            listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = list.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = listContent;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            // ---- render window
            windowScreen = UIFactory.Rect(root, "Window");
            UIFactory.Stretch(windowScreen);

            var spotsButton = UIFactory.Button(windowScreen, "← Spots", CloseWindow, false, Theme.White, 28);
            UIFactory.Place((RectTransform)spotsButton.transform, new Vector2(0, 1), new Vector2(210, 84), new Vector2(32, -32));

            var titleCard = UIFactory.Card(windowScreen, Theme.Yellow, Theme.ShadowMd, "Title");
            UIFactory.Place(titleCard, new Vector2(0.5f, 1), new Vector2(640, 84), new Vector2(0, -32));
            UIFactory.Tilt(titleCard, -1f);
            titleCard.GetComponent<Image>().raycastTarget = false;
            windowTitle = UIFactory.Heading(titleCard, "", 32, TextAnchor.MiddleCenter);
            UIFactory.Stretch(windowTitle.rectTransform, 12);
            UIFactory.FitInside(windowTitle, 18);

            alignButton = UIFactory.Button(windowScreen, "Align", () => SetAligning(!aligning), false, Theme.White, 26);
            UIFactory.Place((RectTransform)alignButton.transform, new Vector2(1, 1), new Vector2(170, 84), new Vector2(-32, -32));
            saveButton = UIFactory.Button(windowScreen, "Save", SaveAlignment, false, Theme.Red, 26);
            UIFactory.Place((RectTransform)saveButton.transform, new Vector2(1, 1), new Vector2(170, 84), new Vector2(-32, -136));
            var hint = UIFactory.Badge(windowScreen, "Drag sideways until the render lines up with the real view", Theme.Ink, 22, 0, Theme.Cream);
            UIFactory.Place(hint, new Vector2(0.5f, 0), new Vector2(960, 56), new Vector2(0, 190));
            hint.GetComponent<Image>().raycastTarget = false;
            alignHint = hint.GetComponentInChildren<Text>();

            // NOW <slider> PROPOSED
            var now = UIFactory.Badge(windowScreen, "Now", Theme.White, 28, -3f);
            UIFactory.Place(now, new Vector2(0.5f, 0), new Vector2(150, 64), new Vector2(-600, 56));
            var proposed = UIFactory.Badge(windowScreen, "Proposed", Theme.Red, 28, 3f);
            UIFactory.Place(proposed, new Vector2(0.5f, 0), new Vector2(220, 64), new Vector2(630, 56));
            slider = UIFactory.Slider(windowScreen, 1f, Theme.Red, v => { if (panoMaterial != null) panoMaterial.SetFloat("_Alpha", v); });
            UIFactory.Place((RectTransform)slider.transform, new Vector2(0.5f, 0), new Vector2(900, 90), new Vector2(0, 44));

            windowScreen.gameObject.SetActive(false);
            SetAligning(false);
        }

        void BuildRows()
        {
            for (int i = 0; i < spots.Count; i++)
            {
                var spot = spots[i];
                var row = UIFactory.Card(listContent, Theme.White, Theme.ShadowSm, "Spot " + spot.def.id);
                UIFactory.Layout(row, preferredHeight: 92);
                var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hl.padding = new RectOffset(12, 14, 10, 10);
                hl.spacing = 14;
                hl.childControlWidth = hl.childControlHeight = true;
                hl.childForceExpandWidth = false;
                hl.childAlignment = TextAnchor.MiddleLeft;

                var num = UIFactory.Card(row, i % 2 == 0 ? Theme.Yellow : Theme.Violet, Vector2.zero, "Number");
                UIFactory.Layout(num, preferredWidth: 70);
                UIFactory.Stretch(UIFactory.Heading(num, (i + 1).ToString("00"), 30, TextAnchor.MiddleCenter).rectTransform);

                var name = UIFactory.Heading(row, UIFactory.Shorten(spot.def.name ?? spot.def.id, 30), 28, TextAnchor.MiddleLeft);
                UIFactory.FitInside(name, 18);
                UIFactory.Layout(name, flexibleWidth: 1);

                var arrowHolder = UIFactory.Rect(row, "Arrow");
                UIFactory.Layout(arrowHolder, preferredWidth: 56);
                var disc = UIFactory.Circle(arrowHolder, 56, Theme.Cream, new Vector2(0.5f, 0.5f), Vector2.zero, shadow: false);
                UIFactory.Stretch(UIFactory.Heading(disc.rectTransform, "↑", 36, TextAnchor.MiddleCenter).rectTransform);

                var dist = UIFactory.Heading(row, "", 26, TextAnchor.MiddleRight);
                UIFactory.Layout(dist, preferredWidth: 140);

                var s = spot;
                UIFactory.Layout(UIFactory.Button(row, "Open", () => Open(s), false, Theme.Red, 24), preferredWidth: 130);
                UIFactory.Layout(UIFactory.Button(row, "Set here", () => SetHere(s), false, Theme.White, 22), preferredWidth: 160);

                rows.Add(new Row { spot = spot, distance = dist, arrow = disc.rectTransform });
            }
        }

        void BuildSphere()
        {
            panoMaterial = Visuals.Panorama();
            panoMaterial.SetFloat("_Alpha", 1f);
            sphere = Visuals.Primitive(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * 100f, panoMaterial, collider: false).transform;
            sphere.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sphere.gameObject.SetActive(false);
        }

        void ShowList()
        {
            listScreen.gameObject.SetActive(true);
        }

        void SetGuide(string title, string detail)
        {
            if (guideTitle == null) return;
            guideTitle.text = title.ToUpperInvariant();
            guideDetail.text = detail;
        }

        void Toast(string text)
        {
            var bg = UIFactory.Card(transform.GetComponentInChildren<Canvas>().transform, Theme.Ink, Vector2.zero, "Toast");
            UIFactory.Place(bg, new Vector2(0.5f, 0.5f), new Vector2(1200, 90), new Vector2(0, 40));
            UIFactory.Tilt(bg, 0.8f);
            bg.GetComponent<Image>().raycastTarget = false;
            var t = UIFactory.Label(bg, text, 24, TextAnchor.MiddleCenter, Theme.Cream);
            UIFactory.Stretch(t.rectTransform, 12);
            UIFactory.FitInside(t, 16);
            Destroy(bg.gameObject, 6f);
        }

        static string FormatDistance(float m) => m < 1000 ? $"{Mathf.RoundToInt(m)} m" : $"{m / 1000f:0.0} km";

        static string Compass(float bearing)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return names[Mathf.RoundToInt(Mathf.Repeat(bearing, 360f) / 45f) % 8];
        }

        void OnDestroy()
        {
            foreach (var tex in loaded.Values) if (tex != null && tex != Texture2D.grayTexture) Destroy(tex);
            if (panoMaterial != null) Destroy(panoMaterial);
            if (XRBootstrap.IsARActive && App.Instance != null) App.Instance.RunNextFrame(XRBootstrap.StopAR);
            if (rig != null) rig.gameObject.SetActive(true);
        }
    }
}
