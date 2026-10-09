using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VRLandscape
{
    // Entry point. Boots itself into whatever scene is loaded, so no scene setup is needed.
    // Flow: park menu -> "Walk in 3D" (model + 360 markers) or "360 Tour" (panorama hops) -> back to menu.
    public partial class App : MonoBehaviour
    {
        public static App Instance { get; private set; }

        PlayerRig rig;
        List<ParkDef> parks = new List<ParkDef>();
        int menuPage;
        int sessionId;
        GameObject session;
        ModelLoader.Result model;
        MainMenu menu;
        PhoneHud hud;
        Canvas messageCanvas;
        float recenterYaw;

        bool IsXR => XRBootstrap.IsXRActive;
        bool InSession => session != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null || FindAnyObjectByType<App>() != null) return;
            new GameObject("VR Landscape App").AddComponent<App>();
        }

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        IEnumerator Start()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            PrepareScene();

            yield return XRBootstrap.TryStart();
            if (!IsXR)
            {
                Screen.orientation = ScreenOrientation.LandscapeLeft;
                Application.targetFrameRate = 60;
            }

            rig = PlayerRig.Create(IsXR);
            if (IsXR) rig.XR.OnBackPressed = OnBack;
            SetupSync();

            ReloadParks();
        }

        void OnApplicationQuit() => XRBootstrap.Stop();

        // Removes the scene's own camera (we bring our own) and makes sure there is light and UI input.
        void PrepareScene()
        {
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None)) Destroy(cam.gameObject);

            if (FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.1f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
                DontDestroyOnLoad(sun.gameObject);
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.80f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.55f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.27f, 0.22f);

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                DontDestroyOnLoad(es);
            }
        }

        void Update()
        {
            // Android back button / Escape
            if (!IsXR && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) OnBack();
            ApplySync();
        }

        void OnBack()
        {
            if (InSession) ShowMenu();
            else if (!IsXR) Application.Quit();
        }

        // ---------------------------------------------------------------- menu

        async void ReloadParks()
        {
            EndSession();
            ShowMessage("Loading parks...");
            try { parks = await ParkCatalog.LoadAll(); }
            catch (Exception e) { Debug.LogException(e); }
            ShowMenu();
        }

        void ShowMenu()
        {
            OnLocalNavigation();
            SetCurrent(null, "menu");
            EndSession();
            HideMessage();
            if (menu != null) Destroy(menu.gameObject);

            rig.MovementEnabled = false;
            rig.Teleport(Vector3.zero, 0);

            // AR and file loading are phone features (no camera passthrough / file picker flow on Quest).
            menu = MainMenu.Create(parks, IsXR, menuPage, StartWalkthrough, p => StartPanorama(p, null),
                IsXR ? null : (Action<ParkDef>)StartAR, IsXR ? null : (Action<ParkDef>)StartSiteTour,
                IsXR ? null : (Action)LoadFile, OpenSyncPanel, DeletePark,
                ReloadParks, page => { menuPage = page; ShowMenu(); });
            if (IsXR) PlaceInFrontOfHead(menu.transform, 2.2f);
        }

        void PlaceInFrontOfHead(Transform t, float distance)
        {
            var head = rig.Cam.transform;
            var forward = head.forward; forward.y = 0;
            if (forward.sqrMagnitude < 0.01f) forward = rig.transform.forward;
            forward.Normalize();
            float eye = Mathf.Max(head.position.y, rig.transform.position.y + 1.2f);
            t.position = new Vector3(head.position.x, eye - 0.15f, head.position.z) + forward * distance;
            t.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        // ---------------------------------------------------------------- sessions

        int BeginSession(string name)
        {
            EndSession();
            if (menu != null) { Destroy(menu.gameObject); menu = null; }
            session = new GameObject(name);
            return ++sessionId;
        }

        void EndSession()
        {
            sessionId++;
            if (hud != null) { Destroy(hud.gameObject); hud = null; }
            model?.Dispose();
            model = null;
            if (session != null) { Destroy(session); session = null; }
        }

        bool Stale(int id) => id != sessionId || this == null;

        async void StartWalkthrough(ParkDef park)
        {
            OnLocalNavigation();
            int id = BeginSession("Walkthrough: " + park.name);
            SetCurrent(park, "walk");
            walkReady = false;
            ShowMessage("Loading " + park.name + "...");
            await Task.Yield();

            ModelLoader.Result loaded;
            try
            {
                loaded = await ModelLoader.Load(park, session.transform);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (!Stale(id)) ShowError("Could not load the 3D model:\n" + e.Message);
                return;
            }
            if (Stale(id)) { loaded.Dispose(); return; }
            model = loaded;
            try
            {
                FinishWalkthrough(park, loaded);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (!Stale(id)) ShowError("Something went wrong opening " + park.name + ":\n" + e.Message);
            }
        }

        void FinishWalkthrough(ParkDef park, ModelLoader.Result loaded)
        {

            var spawn = ModelLoader.FindSpawn(park, loaded.Bounds, out float yaw);
            recenterYaw = yaw;
            rig.Teleport(spawn, yaw);
            rig.SetRespawn(spawn, yaw, loaded.Bounds.min.y - 30f);
            rig.MovementEnabled = true;

            // 360 markers at panorama viewpoints that have a position.
            if (park.HasPanoramas)
                foreach (var node in park.panoramas)
                {
                    if (!ParkDef.HasPosition(node)) continue;
                    var pos = ParkDef.ToVector(node.position, Vector3.zero);
                    var marker = PanoramaViewer.CreateMarker(session.transform, pos + Vector3.up * 0.4f,
                        "360°  " + (node.name ?? node.id), Theme.Violet, 0.35f);
                    string nodeId = node.id;
                    marker.onActivate = () => StartPanorama(park, nodeId);
                }

            HideMessage();
            walkReady = true;
            if (!IsXR) hud = PhoneHud.Create(rig, true, park.name, ShowMenu, () => recenterYaw, OpenSyncPanel);
        }

        async void StartPanorama(ParkDef park, string nodeId)
        {
            OnLocalNavigation();
            int id = BeginSession("360 Tour: " + park.name);
            SetCurrent(park, "pano");
            rig.MovementEnabled = false;

            if (park.isDemo && park.runtimeTextures == null)
            {
                ShowMessage("Rendering demo panoramas...");
                await Task.Yield();
                await Task.Yield();
                if (Stale(id)) return;
                try { DemoPark.RenderPanoramas(park, session.transform); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    park.runtimeTextures = null;
                    ShowError("Couldn't render the demo panoramas:\n" + e.Message);
                    return;
                }
            }

            var node = park.FindPanorama(nodeId ?? park.FirstPanoramaId());
            if (node == null) { ShowError("This park has no panoramas."); return; }

            // Panoramas follow the head, so where the rig stands doesn't matter; face the image centre.
            rig.Teleport(Vector3.zero, node.yawOffset);
            recenterYaw = node.yawOffset;

            var viewer = PanoramaViewer.Create(session.transform, park, rig.Cam);
            currentViewer = viewer;
            viewer.NodeChanged += OnViewerNodeChanged;
            if (!IsXR)
            {
                hud = PhoneHud.Create(rig, false, PhoneHud.TwoPart(park.name, node.name ?? node.id), ShowMenu, () => recenterYaw, OpenSyncPanel);
                var thisHud = hud;
                viewer.NodeChanged += n =>
                {
                    recenterYaw = n.yawOffset;
                    if (thisHud != null) thisHud.SetTitle(PhoneHud.TwoPart(park.name, n.name ?? n.id));
                };
            }

            ShowMessage("Loading panorama...");
            syncNodeChange = true;   // the first viewpoint isn't a user navigation
            try
            {
                await viewer.ShowNode(node.id);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (!Stale(id)) ShowError("Something went wrong opening the 360° view:\n" + e.Message);
                return;
            }
            if (!Stale(id)) HideMessage();
        }

        void StartAR(ParkDef park)
        {
            OnLocalNavigation();
            BeginSession("AR: " + park.name);
            SetCurrent(park, "ar");
            rig.MovementEnabled = false;
            ARSiteWalk.Create(session.transform, park, rig, ShowMenu);
        }

        // On-site mode: walk to each 360 render's real spot; it opens over the live camera when you arrive.
        void StartSiteTour(ParkDef park)
        {
            OnLocalNavigation();
            BeginSession("On site: " + park.name);
            SetCurrent(park, "site");
            rig.MovementEnabled = false;
            SiteTour.Create(session.transform, park, rig, ShowMenu);
        }

        void LoadFile()
        {
            FileImporter.Pick(async path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                ShowMessage("Importing " + System.IO.Path.GetFileName(path) + "...");
                try
                {
                    string name = await FileImporter.Import(path);
                    parks = await ParkCatalog.LoadAll();
                    int index = parks.FindIndex(p => p.baseDir != null && p.baseDir.EndsWith("/" + name));
                    if (index >= 0) menuPage = index / MainMenu.PageSize;
                    ShowMenu();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    ShowError("Could not import the file:\n" + e.Message);
                }
            });
        }

        async void DeletePark(ParkDef park)
        {
            try
            {
                ParkCatalog.Delete(park);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowError("Could not delete " + park.name + ":\n" + e.Message);
                return;
            }
            parks = await ParkCatalog.LoadAll();
            ShowMenu();
        }

        public void RunNextFrame(Action action) => StartCoroutine(NextFrame(action));

        static IEnumerator NextFrame(Action action)
        {
            yield return null;
            action();
        }

        // ---------------------------------------------------------------- messages

        void ShowMessage(string text, bool withBackButton = false)
        {
            HideMessage();
            // A white card with a sticker tag: yellow "Loading" or red "Oops".
            messageCanvas = UIFactory.CreateCanvas("Message", IsXR, new Vector2(980, 440));
            messageCanvas.sortingOrder = 20;
            var card = UIFactory.Card(messageCanvas.transform, Theme.White, Theme.ShadowLg, "Message");
            if (IsXR) UIFactory.Stretch(card, 20);
            else UIFactory.Place(card, new Vector2(0.5f, 0.5f), new Vector2(900, 360), Vector2.zero);
            UIFactory.Tilt(card, -0.8f);

            var tag = UIFactory.Badge(card, withBackButton ? "Oops" : "Loading", withBackButton ? Theme.Red : Theme.Yellow, 30, -4f);
            UIFactory.Place(tag, new Vector2(0, 1), new Vector2(withBackButton ? 150 : 200, 64), new Vector2(-24, 30));

            var label = UIFactory.Label(card, text, 34, TextAnchor.MiddleCenter);
            UIFactory.Stretch(label.rectTransform, 40);
            if (withBackButton)
            {
                label.rectTransform.offsetMin = new Vector2(40, 120);
                var back = UIFactory.Button(card, "← Back to parks", ShowMenu, IsXR, Theme.Red, 28);
                UIFactory.Place((RectTransform)back.transform, new Vector2(0.5f, 0), new Vector2(340, 84), new Vector2(0, 30));
            }
            if (IsXR) PlaceInFrontOfHead(messageCanvas.transform, 1.8f);
        }

        void ShowError(string text) => ShowMessage(text, withBackButton: true);

        void HideMessage()
        {
            if (messageCanvas != null) Destroy(messageCanvas.gameObject);
            messageCanvas = null;
        }
    }
}
