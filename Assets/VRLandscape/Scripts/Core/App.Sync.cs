using UnityEngine;
using UnityEngine.UI;

namespace VRLandscape
{
    // View Sync glue: tells the presenter's ViewSync what is on screen, and makes a following viewer
    // open the same park/mode/viewpoint and mirror the presenter's camera until they touch the screen.
    public partial class App
    {
        ParkDef currentPark;
        string currentMode = "menu";
        string currentNodeId;
        PanoramaViewer currentViewer;
        bool walkReady;

        bool syncNavigating;       // true while the app navigates because of the presenter (not the user)
        bool syncNodeChange;       // the next viewpoint change comes from sync / initial load
        string appliedPark, appliedMode;
        SyncPanel syncPanel;

        void SetupSync()
        {
            var sync = ViewSync.Ensure();
            sync.Capture = CaptureSyncState;
            sync.FollowResumed = () => { appliedPark = null; appliedMode = null; };
            if (rig.Phone != null) rig.Phone.UserInput += OnLocalInput;
            if (rig.XR != null)
            {
                rig.XR.UserInput = OnLocalInput;
                rig.XR.OnPresenterView = () => ViewSync.Instance?.ResumeFollowing();
            }
        }

        void SetCurrent(ParkDef park, string mode)
        {
            currentPark = park;
            currentMode = mode;
            currentNodeId = null;
            currentViewer = null;
        }

        void OnViewerNodeChanged(PanoramaDef node)
        {
            currentNodeId = node.id;
            if (syncNodeChange) syncNodeChange = false;
            else OnLocalNavigation();
        }

        ViewSync.State CaptureSyncState()
        {
            var cam = rig.Cam.transform;
            return new ViewSync.State
            {
                park = currentPark?.name ?? "",
                mode = currentMode,
                node = currentNodeId ?? "",
                position = cam.position,
                rotation = cam.rotation
            };
        }

        // The viewer did something themselves: stop mirroring the presenter.
        void OnLocalInput()
        {
            var sync = ViewSync.Instance;
            if (sync == null || sync.CurrentRole != ViewSync.Role.Follower || sync.Roaming) return;
            sync.Roam();
            ReleaseCamera();
        }

        void OnLocalNavigation()
        {
            if (!syncNavigating) OnLocalInput();
        }

        void ReleaseCamera()
        {
            if (rig.Phone != null && rig.Phone.ExternallyDriven)
            {
                rig.Phone.ExternallyDriven = false;
                rig.Phone.SyncLookFromCamera();
            }
            if (rig.Body != null && !rig.Body.enabled && rig.gameObject.activeInHierarchy) rig.Body.enabled = true;
        }

        void OpenSyncPanel()
        {
            if (syncPanel != null) return;
            syncPanel = SyncPanel.Create(IsXR, () => syncPanel = null);
            if (IsXR) PlaceInFrontOfHead(syncPanel.transform, 2.0f);
        }

        // Called every frame.
        void ApplySync()
        {
            var sync = ViewSync.Instance;
            if (rig == null || sync == null) return;
            bool following = sync.CurrentRole == ViewSync.Role.Follower && sync.HasState;
            if (!following || sync.Roaming)
            {
                ReleaseCamera();
                return;
            }

            var st = sync.Latest;
            if (st.park != appliedPark || st.mode != appliedMode)
            {
                appliedPark = st.park;
                appliedMode = st.mode;
                NavigateTo(st);
                return;
            }

            if (currentMode == "pano" && currentViewer != null && !string.IsNullOrEmpty(st.node) && st.node != currentNodeId)
            {
                syncNodeChange = true;
                _ = currentViewer.ShowNode(st.node);
            }

            if (currentMode == "walk" ? !walkReady : currentMode != "pano") return;

            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            if (rig.Phone != null)
            {
                rig.Phone.ExternallyDriven = true;
                rig.Cam.transform.rotation = Quaternion.Slerp(rig.Cam.transform.rotation, st.rotation, k);
            }
            if (currentMode == "walk")
            {
                // Put our camera where the presenter's camera is (works for phone and headset rigs).
                var target = st.position - (rig.Cam.transform.position - rig.transform.position);
                rig.Body.enabled = false;
                rig.transform.position = (rig.transform.position - target).sqrMagnitude > 25f
                    ? target
                    : Vector3.Lerp(rig.transform.position, target, k);
            }
        }

        void NavigateTo(ViewSync.State st)
        {
            var park = parks.Find(p => p.name == st.park);
            syncNavigating = true;
            try
            {
                switch (st.mode)
                {
                    case "menu":
                        if (InSession) ShowMenu();
                        break;
                    case "walk":
                        if (park != null) StartWalkthrough(park); else SyncToast($"The presenter is showing \"{st.park}\", which isn't on this phone.");
                        break;
                    case "pano":
                        if (park != null) StartPanorama(park, string.IsNullOrEmpty(st.node) ? null : st.node);
                        else SyncToast($"The presenter is showing \"{st.park}\", which isn't on this phone.");
                        break;
                    default:
                        SyncToast(st.mode == "ar"
                            ? "The presenter is in AR. Explore on your own until they come back."
                            : "The presenter is in On-site mode. Explore on your own until they come back.");
                        break;
                }
            }
            finally
            {
                syncNavigating = false;
            }
        }

        void SyncToast(string text)
        {
            var canvas = UIFactory.CreateCanvas("Sync Toast", IsXR, new Vector2(1300, 140));
            canvas.sortingOrder = 40;
            var card = UIFactory.Card(canvas.transform, Theme.Ink, Vector2.zero, "Toast");
            if (IsXR) UIFactory.Stretch(card, 10);
            else UIFactory.Place(card, new Vector2(0.5f, 0.5f), new Vector2(1300, 100), new Vector2(0, -120));
            UIFactory.Tilt(card, 0.8f);
            card.GetComponent<Image>().raycastTarget = false;
            var t = UIFactory.Label(card, text, 26, TextAnchor.MiddleCenter, Theme.Cream);
            UIFactory.Stretch(t.rectTransform, 14);
            UIFactory.FitInside(t, 16);
            if (IsXR) PlaceInFrontOfHead(canvas.transform, 1.8f);
            Destroy(canvas.gameObject, 6f);
        }
    }
}
