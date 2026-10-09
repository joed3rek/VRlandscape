using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace VRLandscape
{
    // AR Site Walk: place a park model on the real ground through the phone camera.
    //  - Tabletop: the whole site as a scale model (pinch to resize).
    //  - Life-size 1:1: walk through the design on site.
    // Ground calibration: live tracking hints while scanning, an "estimated floor" fallback with Floor up/down
    // trim, and Recalibrate. A Measure tool checks the real-world scale against a tape measure.
    // Without ARCore (editor, unsupported phone) it runs a simulated preview on a grey ground plane.
    public class ARSiteWalk : MonoBehaviour
    {
        enum Stage { Starting, Scanning, Placing, Placed, Failed }

        const float LifeSizeMaxFootprint = 30f;   // models smaller than this open life-size by default
        const float TabletopSize = 0.8f;          // metres across for a tabletop model
        static readonly float[] OpacitySteps = { 1f, 0.7f, 0.4f };
        const float HoldHeight = 1.4f;            // typical phone height above the ground when held to look at it
        const float EstimateAfterSeconds = 6f;    // offer the estimated floor if no ground is found by then
        const float FloorTapStep = 0.02f;         // one tap on Floor up/down
        const float FloorMinSpeed = 0.1f;         // m/s when a hold starts...
        const float FloorMaxSpeed = 1.0f;         // ...ramping up to this
        static readonly int[] ScalePresets = { 50, 100, 200, 500, 1000 };   // tabletop 1:N
        const float TabletopFallbackDistance = 0.6f;   // tap with nothing detected: place this far along the tap ray
        const float LifeSizeFallbackDistance = 2.5f;

        ParkDef park;
        PlayerRig rig;
        Action onExit;

        Stage stage = Stage.Starting;
        bool simulated = true;
        string simulatedReason;
        Camera cam;

        // AR Foundation
        GameObject arRoot;
        ARSession arSession;
        ARPlaneManager planeManager;
        ARAnchorManager anchorManager;
        ARAnchor arAnchor;          // ARCore anchor the model is pinned to (keeps it fixed as tracking refines)
        ARPlane lastHitPlane, reticlePlane;
        int anchorToken;            // cancels a pending free-anchor request when the model is re-placed
        // The model follows its ARAnchor (instead of being parented to it) so it never disappears if ARCore
        // removes or replaces the anchor; offset = model pose relative to the anchor, refreshed after every edit.
        Vector3 anchorOffsetPosition;
        Quaternion anchorOffsetRotation = Quaternion.identity;
        bool modelMoved;
        ARCameraPoseFallback poseFallback;
        ARRaycastManager raycaster;
        AROcclusionManager occlusion;
        Material planeMaterial;
        readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        bool occlusionOn;   // off by default: depth on mid-range phones is noisy and can make the model flicker away

        // Model placement
        Transform anchor, holder, reticle, catcher;
        ModelLoader.Result model;
        OpacityController opacity;
        Bounds bounds;
        Vector3 spawnLocal;
        float spawnYaw;
        bool lifeSize;
        float tableScale = 0.01f, userScale = 1f;
        int opacityStep;
        Pose reticlePose;
        bool hasReticle;
        float savedShadowDistance;

        // Ground calibration
        bool useEstimatedFloor;
        float floorY;
        float scanStart;

        // Measure tool
        bool measuring;
        readonly List<Vector3> measurePoints = new List<Vector3>();
        Transform measureRoot;
        LineRenderer measureLine;

        // HUD
        Text status, scaleLabel, measureLabel;
        Button placeButton, modeButton, recalibrateButton, opacityButton, occlusionButton, measureButton,
            estimateButton, floorUpButton, floorDownButton, snapButton;
        Text heightLabel, trackingLabel;
        Image trackingBadge;
        float heightOffset;   // how far the model was raised/lowered since it was placed
        bool adjusting;       // gestures move/rotate/scale only while ADJUST is on, so stray touches can't shift it
        Button adjustButton, scaleButton, scaleUpButton, scaleDownButton;
        RectTransform diagnostics;
        Text diagnosticsText;
        RectTransform engineBadge;

        public static ARSiteWalk Create(Transform parent, ParkDef park, PlayerRig rig, Action onExit)
        {
            var go = new GameObject("AR Site Walk");
            go.transform.SetParent(parent, false);
            var walk = go.AddComponent<ARSiteWalk>();
            walk.park = park;
            walk.rig = rig;
            walk.onExit = onExit;
            return walk;
        }

        IEnumerator Start()
        {
            savedShadowDistance = QualitySettings.shadowDistance;
            QualitySettings.shadowDistance = 80f;
            BuildHud();
            SetStatus("Starting AR...");

            yield return StartARCore();

            if (simulated)
            {
                cam = rig.Cam;
                rig.Teleport(Vector3.zero, 0);
                rig.MovementEnabled = true;
                var ground = Visuals.Primitive(PrimitiveType.Cube, transform, new Vector3(0, -0.05f, 0), new Vector3(200, 0.1f, 200),
                    Visuals.Lit(new Color(0.52f, 0.53f, 0.50f)));
                ground.name = "Simulated Ground";
            }

            // Load the model at the origin first, so its spawn point and bounds can be measured.
            anchor = new GameObject("AR Anchor").transform;
            anchor.SetParent(transform, false);
            holder = new GameObject("Model Holder").transform;
            holder.SetParent(anchor, false);
            SetStatus("Loading " + park.name + "...");

            var load = ModelLoader.Load(park, holder);
            while (!load.IsCompleted) yield return null;
            if (load.IsFaulted)
            {
                Fail("Could not load the model:\n" + load.Exception?.GetBaseException().Message);
                yield break;
            }
            model = load.Result;
            bounds = model.Bounds;
            spawnLocal = ModelLoader.FindSpawn(park, bounds, out spawnYaw);
            opacity = OpacityController.Attach(model.Root);
            catcher = BuildShadowCatcher();

            float footprint = Mathf.Max(bounds.size.x, bounds.size.z, 0.01f);
            tableScale = TabletopSize / footprint;
            lifeSize = footprint <= LifeSizeMaxFootprint;
            anchor.gameObject.SetActive(false);

            reticle = BuildReticle();
            BuildMeasureTool();
            SetEngineBadge();
            EnterPlacing();
            if (simulated) ShowToast(simulatedReason);
            else if (model.Note != null) ShowToast(model.Note);
        }

        IEnumerator StartARCore()
        {
            ARRigFactory.Rig arRig = null;
            yield return ARRigFactory.Start(transform, AddARComponents, SetStatus, r => arRig = r);
            simulated = arRig == null;
            if (simulated)
            {
#if UNITY_EDITOR
                simulatedReason = "Editor preview of AR: WASD walks, drag looks, IJKL moves the model, U/O rotates, -/= scales.";
#else
                simulatedReason = "AR isn't available on this phone, so this is a simulated preview.";
#endif
                yield break;
            }
            arRoot = arRig.root;
            arSession = arRig.session;
            cam = arRig.camera;
            poseFallback = cam.GetComponent<ARCameraPoseFallback>();
            rig.gameObject.SetActive(false);
        }

        // Plane detection, raycasts and depth occlusion on top of the shared camera rig.
        void AddARComponents(GameObject originGo, GameObject camGo)
        {
            occlusion = camGo.AddComponent<AROcclusionManager>();
            occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
            raycaster = originGo.AddComponent<ARRaycastManager>();
            anchorManager = originGo.AddComponent<ARAnchorManager>();
            anchorManager.trackablesChanged.AddListener(OnAnchorsChanged);
            // Tabletop distances need autofocus for ARCore to track texture sharply.
            var cameraManager = camGo.GetComponent<ARCameraManager>();
            if (cameraManager != null) cameraManager.autoFocusRequested = true;
            planeManager = originGo.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            planeMaterial = Visuals.Unlit(new Color(Theme.Yellow.r, Theme.Yellow.g, Theme.Yellow.b, 0.35f));
            planeManager.trackablesChanged.AddListener(OnPlanesChanged);
        }

        void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            foreach (var plane in args.added)
            {
                var mr = plane.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = planeMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                plane.gameObject.AddComponent<ARPlaneMeshVisualizer>();
            }
        }

        void ShowPlanes(bool visible)
        {
            if (planeMaterial != null) planeMaterial.color = new Color(Theme.Yellow.r, Theme.Yellow.g, Theme.Yellow.b, visible ? 0.35f : 0f);
        }

        // ------------------------------------------------------------ placement

        void Update()
        {
            if (stage == Stage.Scanning || stage == Stage.Placing)
            {
                hasReticle = Hit(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out reticlePose);
                reticlePlane = lastHitPlane;
                reticle.gameObject.SetActive(hasReticle);
                if (hasReticle)
                {
                    reticle.position = reticlePose.position + Vector3.up * 0.005f;
                    reticle.rotation = Quaternion.Euler(0, cam.transform.eulerAngles.y, 0);
                }
                var next = hasReticle ? Stage.Placing : Stage.Scanning;
                if (next != stage) { stage = next; RefreshHud(); }
                if (stage == Stage.Scanning && !measuring) ShowScanHint();

                bool offerEstimate = !simulated && !useEstimatedFloor && stage == Stage.Scanning &&
                                     Time.time - scanStart > EstimateAfterSeconds;
                if (estimateButton.gameObject.activeSelf != offerEstimate) estimateButton.gameObject.SetActive(offerEstimate);
            }
            else if (stage == Stage.Placed && simulated)
            {
                KeyboardAdjust();
            }
            UpdateTrackingBadge();
            if (diagnostics != null && diagnostics.gameObject.activeSelf) UpdateDiagnostics();
        }

        void UpdateDiagnostics()
        {
            if (cam == null) return;
            var p = cam.transform.localPosition;
            int planes = planeManager != null ? planeManager.trackables.count : 0;
            string anchorState = arAnchor != null ? arAnchor.trackingState.ToString() : (stage == Stage.Placed ? "none (estimated floor)" : "-");
            string dist = stage == Stage.Placed && anchor != null
                ? Vector3.Distance(cam.transform.position, anchor.position).ToString("0.00") + " m"
                : "-";
            diagnosticsText.text =
                $"AR: {ARSession.state}  ({ARSession.notTrackingReason})\n" +
                $"Camera: {p.x:0.00}, {p.y:0.00}, {p.z:0.00}  yaw {cam.transform.eulerAngles.y:0}\n" +
                $"Pose source: {(poseFallback != null && poseFallback.Active ? "XR fallback" : "Input System")}\n" +
                $"Surfaces found: {planes}   Anchor: {anchorState}\n" +
                $"Distance to model: {dist}\n" +
                "Camera should change as you move. Distance should match reality.";
        }

        void UpdateTrackingBadge()
        {
            if (trackingLabel == null || simulated || arSession == null) return;
            bool tracking = ARSession.state == ARSessionState.SessionTracking;
            string text = tracking ? (poseFallback != null && poseFallback.Active ? "TRACKING (XR)" : "TRACKING") : "LOST";
            if (trackingLabel.text != text)
            {
                trackingLabel.text = text;
                trackingBadge.color = tracking ? Theme.Yellow : Theme.Red;
                if (stage == Stage.Placed && !measuring)
                {
                    if (tracking) RefreshHud();
                    else SetStatus("Tracking lost: " + NotTrackingHint() + ". The model will settle when it's back");
                }
            }
        }

        bool Hit(Vector2 screen, out Pose pose)
        {
            pose = default;
            lastHitPlane = null;
            if (cam == null) return false;
            if (simulated) return RayToFloor(screen, 0f, out pose);
            // Only surfaces ARCore has actually mapped: "estimated" planes have unreliable depth, which made the
            // model land on the floor behind a table (wrong size when moving closer, sliding when looking away).
            // Tabletop: take the NEAREST real hit (surface or tracked point). Preferring planes made taps on a
            // small table/stool pass straight through to the floor plane behind it (model 1.9 m away, looking
            // like it floated above the table).
            if (!lifeSize && raycaster != null &&
                raycaster.Raycast(screen, hits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint | TrackableType.Depth))
            {
                var nearest = hits[0];   // AR Foundation sorts hits by distance
                pose = nearest.hitType == TrackableType.PlaneWithinPolygon ? nearest.pose : new Pose(nearest.pose.position, Quaternion.identity);
                lastHitPlane = nearest.trackable as ARPlane;
                return true;
            }
            if (raycaster != null && raycaster.Raycast(screen, hits, TrackableType.PlaneWithinPolygon))
            {
                pose = hits[0].pose;
                lastHitPlane = hits[0].trackable as ARPlane;
                return true;
            }
            // Next best: ARCore's depth map / tracked feature points on the surface under the finger.
            if (raycaster != null && raycaster.Raycast(screen, hits, TrackableType.Depth | TrackableType.FeaturePoint))
            {
                pose = new Pose(hits[0].pose.position, Quaternion.identity);
                return true;
            }
            return useEstimatedFloor && RayToFloor(screen, floorY, out pose);
        }

        bool RayToFloor(Vector2 screen, float y, out Pose pose)
        {
            pose = default;
            var ray = cam.ScreenPointToRay(screen);
            if (ray.direction.y > -0.02f) return false;
            float t = (y - ray.origin.y) / ray.direction.y;
            if (t <= 0f || t > 150f) return false;
            pose = new Pose(ray.origin + ray.direction * t, Quaternion.identity);
            return true;
        }

        static string NotTrackingHint()
        {
            switch (ARSession.notTrackingReason)
            {
                case NotTrackingReason.InsufficientLight: return "too dark, turn on more lights";
                case NotTrackingReason.ExcessiveMotion: return "moving too fast, move slowly";
                case NotTrackingReason.InsufficientFeatures: return "point at something with texture";
                case NotTrackingReason.Relocalizing: return "re-finding your position, look around slowly";
                default: return "hold steady and look around slowly";
            }
        }

        // Live feedback while ARCore looks for the ground.
        void ShowScanHint()
        {
            if (simulated || arSession == null) return;
            string msg;
            if (ARSession.state < ARSessionState.SessionTracking)
            {
                switch (ARSession.notTrackingReason)
                {
                    case NotTrackingReason.InsufficientLight: msg = "Too dark: find more light"; break;
                    case NotTrackingReason.ExcessiveMotion: msg = "Moving too fast: move the phone slowly"; break;
                    case NotTrackingReason.InsufficientFeatures: msg = "Point at textured ground (grass, paving). Plain or shiny floors are hard"; break;
                    case NotTrackingReason.Relocalizing: msg = "Re-finding your position..."; break;
                    case NotTrackingReason.CameraUnavailable: msg = "Camera unavailable: close other camera apps"; break;
                    default: msg = "Starting the camera..."; break;
                }
            }
            else
            {
                int planes = planeManager != null ? planeManager.trackables.count : 0;
                msg = planes == 0
                    ? (lifeSize
                        ? "Scanning: point at the ground and sweep the phone slowly side to side"
                        : "Scanning: point at the table from 40-60 cm and move the phone slowly side to side")
                    : $"{planes} surface(s) found. Tap a YELLOW patch to place the model";
            }
            SetStatus(msg);
        }

        void UseEstimatedFloor()
        {
            useEstimatedFloor = true;
            floorY = cam.transform.position.y - HoldHeight;
            ShowToast("Using an estimated floor 1.4 m below the phone. Use FLOOR ↑ / ↓ to match the real ground.");
            RefreshHud();
        }

        void NudgeFloor(float dy)
        {
            floorY += dy;
            if (stage != Stage.Placed) return;
            anchor.position += Vector3.up * dy;
            modelMoved = true;
            heightOffset += dy;
            heightLabel.text = (heightOffset >= 0 ? "+" : "") + heightOffset.ToString("0.00") + " m";
        }

        // Drops the model onto the ground detected directly beneath it.
        void SnapToGround()
        {
            if (stage != Stage.Placed) return;
            float? groundY = null;
            if (simulated) groundY = 0f;
            else if (raycaster != null &&
                     raycaster.Raycast(new Ray(anchor.position + Vector3.up * 3f, Vector3.down), hits,
                         TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated))
                groundY = hits[0].pose.position.y;

            if (groundY == null)
            {
                ShowToast("No ground found under the model yet. Sweep the phone over the ground there and try again");
                return;
            }
            NudgeFloor(groundY.Value - anchor.position.y);
        }

        // Starts ground detection over: clears ARCore's planes and tracking, then place again.
        void Recalibrate()
        {
            if (!simulated && arSession != null) arSession.Reset();
            useEstimatedFloor = false;
            SetMeasuring(false);
            EnterPlacing();
        }

        void EnterPlacing()
        {
            stage = Stage.Scanning;
            scanStart = Time.time;
            DetachARAnchor();
            adjusting = false;
            anchor.gameObject.SetActive(false);
            ShowPlanes(true);
            RefreshHud();
        }

        void Place(Pose pose) => Place(pose, null);

        // Tap-to-place: a detected surface if there is one, else ARCore depth/feature points, else a point
        // straight ahead along the tap (pinned with an anchor). Placement never just fails.
        const float TabletopFarWarning = 1.4f;

        void PlaceAt(Vector2 screen)
        {
            PlaceAtInner(screen);
            if (stage == Stage.Placed && !lifeSize && !simulated && cam != null)
            {
                float d = Vector3.Distance(cam.transform.position, anchor.position);
                if (d > TabletopFarWarning)
                    ShowToast($"That's {d:0.0} m away, probably the floor behind. For a table, tap a YELLOW patch on it (or RECALIBRATE).");
            }
        }

        void PlaceAtInner(Vector2 screen)
        {
            if (!simulated && ARSession.state != ARSessionState.SessionTracking)
            {
                ShowToast("Hold on: AR isn't tracking yet. Move the phone slowly over the table or ground first.");
                return;
            }
            if (Hit(screen, out var pose)) { Place(pose, lastHitPlane); return; }
            if (cam == null) return;
            if (lifeSize && RayToFloor(screen, cam.transform.position.y - HoldHeight, out var floorPose))
            {
                Place(floorPose, null);
                ShowToast("No ground detected there yet, so the model is on an estimated floor. Use SNAP or FLOOR to adjust.");
                return;
            }
            var ray = cam.ScreenPointToRay(screen);
            Place(new Pose(ray.GetPoint(lifeSize ? LifeSizeFallbackDistance : TabletopFallbackDistance), Quaternion.identity), null);
            ShowToast(lifeSize
                ? "Placed ahead of you. Use SNAP or FLOOR to set it on the ground."
                : "Placed where you tapped, 60 cm away. Use SNAP or FLOOR to set it on the table.");
        }

        void Place(Pose pose, ARPlane plane)
        {
            anchor.gameObject.SetActive(true);
            anchor.position = pose.position;
            floorY = pose.position.y;
            heightOffset = 0f;
            if (heightLabel != null) heightLabel.text = "+0.00 m";
            anchor.rotation = Quaternion.Euler(0, cam.transform.eulerAngles.y - spawnYaw, 0);
            ApplyScale();
            AttachToARAnchor(plane, pose);
            reticle.gameObject.SetActive(false);
            ShowPlanes(false);
            adjusting = false;
            stage = Stage.Placed;
            RefreshHud();
        }

        // Pins the model to an ARCore anchor on the detected ground, so it stays put while ARCore
        // refines its map of the room instead of drifting with it.
        // As in Unity's ARPlaceAnchor sample: attach to the surface when the platform supports it,
        // otherwise create a free anchor asynchronously.
        void AttachToARAnchor(ARPlane plane, Pose pose)
        {
            DetachARAnchor();
            if (simulated || anchorManager == null) return;
            bool canAttach = anchorManager.descriptor != null && anchorManager.descriptor.supportsTrackableAttachments;
            if (plane != null && canAttach)
            {
                arAnchor = anchorManager.AttachAnchor(plane, pose);
                if (arAnchor != null) RebaseOnAnchor();
                else AttachFreeAnchor(pose);
            }
            else AttachFreeAnchor(pose);
        }

        void RebaseOnAnchor()
        {
            if (arAnchor == null || anchor == null) return;
            var inv = Quaternion.Inverse(arAnchor.transform.rotation);
            anchorOffsetPosition = inv * (anchor.position - arAnchor.transform.position);
            anchorOffsetRotation = inv * anchor.rotation;
        }

        // Keeps the model glued to its anchor as ARCore refines the map; edits made this frame re-base instead.
        void LateUpdate()
        {
            if (stage != Stage.Placed || anchor == null) return;
            if (modelMoved) { modelMoved = false; RebaseOnAnchor(); return; }
            if (arAnchor == null || arAnchor.trackingState != TrackingState.Tracking) return;
            var t = arAnchor.transform;
            anchor.SetPositionAndRotation(t.position + t.rotation * anchorOffsetPosition, t.rotation * anchorOffsetRotation);
        }

        // ARCore can drop anchors (session reset, lost map). Keep the model where it is and re-anchor it.
        void OnAnchorsChanged(ARTrackablesChangedEventArgs<ARAnchor> args)
        {
            foreach (var removed in args.removed)
            {
                if (arAnchor == null || removed.Key != arAnchor.trackableId) continue;
                arAnchor = null;
                if (stage == Stage.Placed && !simulated) AttachFreeAnchor(new Pose(anchor.position, anchor.rotation));
            }
        }

        // Anchor in free space (no surface): ARCore still keeps it fixed in the room.
        async void AttachFreeAnchor(Pose pose)
        {
            int token = anchorToken;
            var result = await anchorManager.TryAddAnchorAsync(pose);
            if (this == null || token != anchorToken)
            {
                if (result.status.IsSuccess() && result.value != null && anchorManager != null) anchorManager.TryRemoveAnchor(result.value);
                return;
            }
            if (!result.status.IsSuccess() || result.value == null) return;
            arAnchor = result.value;
            RebaseOnAnchor();
        }

        void DetachARAnchor()
        {
            anchorToken++;
            if (arAnchor != null)
            {
                if (anchorManager == null || !anchorManager.TryRemoveAnchor(arAnchor)) Destroy(arAnchor.gameObject);
            }
            arAnchor = null;
        }

        void ToggleAdjust()
        {
            adjusting = !adjusting;
            RefreshHud();
        }

        // Tabletop: tap the scale badge to step through 1:50 ... 1:1000.
        void CycleScale()
        {
            if (lifeSize) return;
            int current = Mathf.RoundToInt(1f / (tableScale * userScale));
            int next = ScalePresets[0];
            foreach (var n in ScalePresets) if (n > current + 1) { next = n; break; }
            userScale = (1f / next) / tableScale;
            if (stage == Stage.Placed) ApplyScale(); else RefreshHud();
        }

        void ScaleBy(float amount)
        {
            if (lifeSize) return;
            userScale = Mathf.Clamp(userScale * (1f + amount), 0.02f, 50f);
            if (stage == Stage.Placed) ApplyScale(); else RefreshHud();
        }

        // Life-size: the park's spawn point sits on the placed spot. Tabletop: the model's base centre does.
        void ApplyScale()
        {
            var pivot = lifeSize ? spawnLocal : new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            holder.localPosition = -pivot;
            float s = lifeSize ? 1f : tableScale * userScale;
            anchor.localScale = Vector3.one * s;

            var c = bounds.center - pivot;
            catcher.localPosition = new Vector3(c.x, 0.003f, c.z);
            float margin = lifeSize ? 20f : 0f;
            catcher.localScale = new Vector3(bounds.size.x * 1.3f + margin, bounds.size.z * 1.3f + margin, 1f);
            RefreshHud();
        }

        void Pan(Vector2 from, Vector2 to)
        {
            if (simulated) { rig.Phone.Look(to - from); return; }
            if (stage != Stage.Placed || measuring || !adjusting) return;
            var plane = new Plane(Vector3.up, anchor.position);
            var a = cam.ScreenPointToRay(from);
            var b = cam.ScreenPointToRay(to);
            if (plane.Raycast(a, out float ta) && plane.Raycast(b, out float tb) && ta < 200f && tb < 200f)
            {
                var delta = b.GetPoint(tb) - a.GetPoint(ta);
                anchor.position += new Vector3(delta.x, 0, delta.z);
                modelMoved = true;
            }
        }

        void Pinch(float factor)
        {
            if (stage != Stage.Placed || lifeSize || measuring || (!adjusting && !simulated)) return;
            userScale = Mathf.Clamp(userScale * factor, 0.1f, 20f);
            ApplyScale();
        }

        void Twist(float degrees)
        {
            if (stage != Stage.Placed || measuring || (!adjusting && !simulated)) return;
            anchor.Rotate(0, degrees, 0, Space.World);
            modelMoved = true;
        }

        void Tap(Vector2 screen)
        {
            if (measuring) { AddMeasurePoint(screen); return; }
            if (stage == Stage.Scanning || stage == Stage.Placing) PlaceAt(screen);
            else if (stage == Stage.Placed && adjusting && !simulated) PlaceAt(screen);   // move it to the tapped spot
        }

        void KeyboardAdjust()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            var forward = cam.transform.forward; forward.y = 0; forward.Normalize();
            var right = cam.transform.right; right.y = 0; right.Normalize();
            var move = Vector3.zero;
            if (kb.iKey.isPressed) move += forward;
            if (kb.kKey.isPressed) move -= forward;
            if (kb.lKey.isPressed) move += right;
            if (kb.jKey.isPressed) move -= right;
            anchor.position += move * (lifeSize ? 2f : 0.5f) * Time.deltaTime;
            modelMoved = true;
            if (kb.uKey.isPressed) Twist(-60f * Time.deltaTime);
            if (kb.oKey.isPressed) Twist(60f * Time.deltaTime);
            if (kb.minusKey.isPressed) Pinch(1f - Time.deltaTime);
            if (kb.equalsKey.isPressed) Pinch(1f + Time.deltaTime);
        }

        // ------------------------------------------------------------ HUD actions

        void ToggleMode()
        {
            lifeSize = !lifeSize;
            userScale = 1f;
            if (stage == Stage.Placed) ApplyScale();
            RefreshHud();
        }

        void CycleOpacity()
        {
            opacityStep = (opacityStep + 1) % OpacitySteps.Length;
            opacity.SetOpacity(OpacitySteps[opacityStep]);
            RefreshHud();
        }

        void ToggleOcclusion()
        {
            occlusionOn = !occlusionOn;
            if (occlusion != null)
                occlusion.requestedEnvironmentDepthMode = occlusionOn ? EnvironmentDepthMode.Fastest : EnvironmentDepthMode.Disabled;
            RefreshHud();
        }

        void SetMeasuring(bool on)
        {
            measuring = on;
            measurePoints.Clear();
            if (measureRoot != null) measureRoot.gameObject.SetActive(false);
            if (measureLabel != null) measureLabel.transform.parent.gameObject.SetActive(false);
            RefreshHud();
            if (on) SetStatus("Measure: tap two points on the ground. Check the distance with a tape measure");
        }

        void AddMeasurePoint(Vector2 screen)
        {
            if (!Hit(screen, out var pose)) { ShowToast("No ground there yet. Aim at the floor"); return; }
            if (measurePoints.Count >= 2) measurePoints.Clear();
            measurePoints.Add(pose.position + Vector3.up * 0.01f);

            measureRoot.gameObject.SetActive(true);
            var a = measurePoints[0];
            var b = measurePoints[measurePoints.Count - 1];
            measureRoot.GetChild(0).position = a;
            measureRoot.GetChild(1).position = b;
            measureLine.SetPosition(0, a);
            measureLine.SetPosition(1, b);

            bool done = measurePoints.Count == 2;
            measureLabel.transform.parent.gameObject.SetActive(done);
            if (done)
            {
                float metres = Vector3.Distance(a, b);
                measureLabel.text = $"{metres:0.00} m";
                if (stage == Stage.Placed && !lifeSize)
                    measureLabel.text += $"  (= {metres / (tableScale * userScale):0.#} m in the model)";
            }
        }

        void Fail(string message)
        {
            stage = Stage.Failed;
            SetStatus(message);
            RefreshHud();
        }

        // ------------------------------------------------------------ visuals

        Transform BuildReticle()
        {
            var r = new GameObject("Reticle").transform;
            r.SetParent(transform, false);
            var ring = Visuals.Primitive(PrimitiveType.Cylinder, r, Vector3.zero, new Vector3(0.5f, 0.002f, 0.5f),
                Visuals.Unlit(new Color(1f, 1f, 1f, 0.85f)), collider: false);
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var inner = Visuals.Primitive(PrimitiveType.Cylinder, r, new Vector3(0, 0.002f, 0), new Vector3(0.4f, 0.002f, 0.4f),
                Visuals.Unlit(new Color(0.2f, 0.45f, 0.3f, 0.6f)), collider: false);
            inner.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var arrow = Visuals.Primitive(PrimitiveType.Cube, r, new Vector3(0, 0.004f, 0.12f), new Vector3(0.05f, 0.002f, 0.18f),
                Visuals.Unlit(Color.white), collider: false);
            arrow.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.gameObject.SetActive(false);
            return r;
        }

        void BuildMeasureTool()
        {
            measureRoot = new GameObject("Measure").transform;
            measureRoot.SetParent(transform, false);
            foreach (var n in new[] { "A", "B" })
            {
                var dot = Visuals.Primitive(PrimitiveType.Sphere, measureRoot, Vector3.zero, Vector3.one * 0.05f,
                    Visuals.Unlit(Theme.Ink), collider: false);
                dot.name = n;
            }
            measureLine = measureRoot.gameObject.AddComponent<LineRenderer>();
            measureLine.useWorldSpace = true;
            measureLine.positionCount = 2;
            measureLine.startWidth = measureLine.endWidth = 0.015f;
            measureLine.sharedMaterial = Visuals.Unlit(Theme.Red);
            measureLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            measureRoot.gameObject.SetActive(false);
        }

        void SetEngineBadge()
        {
            var t = engineBadge.GetComponentInChildren<Text>();
            t.text = simulated ? "PREVIEW" : "ARCORE";
            engineBadge.GetComponent<Image>().color = simulated ? Theme.Red : Theme.Yellow;
            engineBadge.gameObject.SetActive(true);
        }

        Transform BuildShadowCatcher()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "Shadow Catcher";
            quad.transform.SetParent(anchor, false);
            quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = new Material(Resources.Load<Shader>("VRLandscape/ShadowCatcher"));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            if (simulated) quad.SetActive(false); // the grey simulated ground already shows shadows
            return quad.transform;
        }

        void BuildHud()
        {
            var canvas = UIFactory.CreateCanvas("AR HUD", world: false);
            canvas.transform.SetParent(transform, false);
            var root = canvas.transform;

            var gestures = GestureLayer.Create(root);
            gestures.OnPan = Pan;
            gestures.OnPinch = Pinch;
            gestures.OnTwist = Twist;
            gestures.OnTap = Tap;

            var back = UIFactory.Button(root, "← Parks", () => onExit?.Invoke(), false, Theme.White, 28);
            UIFactory.Place((RectTransform)back.transform, new Vector2(0, 1), new Vector2(210, 84), new Vector2(32, -32));

            var statusCard = UIFactory.Card(root, Theme.White, Theme.ShadowMd, "Status");
            UIFactory.Place(statusCard, new Vector2(0.5f, 1), new Vector2(940, 110), new Vector2(0, -32));
            UIFactory.Tilt(statusCard, -0.6f);
            statusCard.GetComponent<Image>().raycastTarget = false;
            status = UIFactory.Label(statusCard, "", 30, TextAnchor.MiddleCenter);
            UIFactory.Stretch(status.rectTransform, 16);

            modeButton = UIFactory.Button(root, "", ToggleMode, false, Theme.Yellow, 26);
            UIFactory.Place((RectTransform)modeButton.transform, new Vector2(1, 1), new Vector2(290, 84), new Vector2(-32, -32));
            scaleButton = UIFactory.Button(root, "", CycleScale, false, Theme.Violet, 26);
            UIFactory.Place((RectTransform)scaleButton.transform, new Vector2(1, 1), new Vector2(220, 70), new Vector2(-32, -132));
            scaleLabel = scaleButton.GetComponentInChildren<Text>();

            var tracking = UIFactory.Badge(root, "", Theme.Yellow, 18, 2f);
            UIFactory.Place(tracking, new Vector2(0, 1), new Vector2(170, 40), new Vector2(40, -196));
            tracking.GetComponent<Image>().raycastTarget = false;
            trackingBadge = tracking.GetComponent<Image>();
            trackingLabel = tracking.GetComponentInChildren<Text>();

            // Tabletop scale on the left edge: hold to grow/shrink smoothly, tap for 10%.
            scaleUpButton = UIFactory.Button(root, "Scale +", null, false, Theme.White, 24);
            UIFactory.Place((RectTransform)scaleUpButton.transform, new Vector2(0, 0.5f), new Vector2(170, 84), new Vector2(32, 52));
            HoldRepeat.Attach(scaleUpButton, ScaleBy, 0.1f, 0.3f, 1.5f);
            scaleDownButton = UIFactory.Button(root, "Scale -", null, false, Theme.White, 24);
            UIFactory.Place((RectTransform)scaleDownButton.transform, new Vector2(0, 0.5f), new Vector2(170, 84), new Vector2(32, -52));
            HoldRepeat.Attach(scaleDownButton, a => ScaleBy(-a / (1f + a)), 0.1f, 0.3f, 1.5f);

            engineBadge = UIFactory.Badge(root, "", Theme.Yellow, 20, -3f);
            UIFactory.Place(engineBadge, new Vector2(0, 1), new Vector2(150, 46), new Vector2(40, -140));
            var badgeButton = engineBadge.gameObject.AddComponent<Button>();
            badgeButton.targetGraphic = engineBadge.GetComponent<Image>();
            badgeButton.onClick.AddListener(() => diagnostics.gameObject.SetActive(!diagnostics.gameObject.activeSelf));
            engineBadge.gameObject.SetActive(false);

            // Live AR numbers (tap the ARCORE badge). Useful to check tracking and model distance.
            diagnostics = UIFactory.Card(root, Theme.White, Theme.ShadowSm, "Diagnostics");
            UIFactory.Place(diagnostics, new Vector2(0, 1), new Vector2(560, 250), new Vector2(40, -250));
            diagnostics.GetComponent<Image>().raycastTarget = false;
            diagnosticsText = UIFactory.Label(diagnostics, "", 22, TextAnchor.UpperLeft);
            UIFactory.Stretch(diagnosticsText.rectTransform, 16);
            diagnostics.gameObject.SetActive(false);

            // Placing: [Use estimated floor] [PLACE HERE] [Measure]
            placeButton = UIFactory.Button(root, "Place here", () => { if (hasReticle) Place(reticlePose, reticlePlane); else PlaceAt(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)); }, false, Theme.Red, 40);
            UIFactory.Place((RectTransform)placeButton.transform, new Vector2(0.5f, 0), new Vector2(360, 116), new Vector2(0, 40));
            estimateButton = UIFactory.Button(root, "Use estimated floor", UseEstimatedFloor, false, Theme.Yellow, 24);
            UIFactory.Place((RectTransform)estimateButton.transform, new Vector2(0.5f, 0), new Vector2(330, 96), new Vector2(-440, 40));
            estimateButton.gameObject.SetActive(false);

            // Placed: [Adjust] [Recalibrate] [Opacity] [Occlusion] [Measure]
            adjustButton = UIFactory.Button(root, "Adjust", ToggleAdjust, false, Theme.White, 24);
            UIFactory.Place((RectTransform)adjustButton.transform, new Vector2(0.5f, 0), new Vector2(226, 96), new Vector2(-484, 40));
            recalibrateButton = UIFactory.Button(root, "Recalibrate", Recalibrate, false, Theme.White, 22);
            opacityButton = UIFactory.Button(root, "", CycleOpacity, false, Theme.Violet, 22);
            UIFactory.Place((RectTransform)opacityButton.transform, new Vector2(0.5f, 0), new Vector2(226, 96), new Vector2(0, 40));
            occlusionButton = UIFactory.Button(root, "", ToggleOcclusion, false, Theme.White, 22);
            UIFactory.Place((RectTransform)occlusionButton.transform, new Vector2(0.5f, 0), new Vector2(226, 96), new Vector2(242, 40));
            measureButton = UIFactory.Button(root, "Measure", () => SetMeasuring(!measuring), false, Theme.White, 24);
            UIFactory.Place((RectTransform)measureButton.transform, new Vector2(0.5f, 0), new Vector2(226, 96), new Vector2(484, 40));

            // Floor trim on the right edge: hold to glide (speeds up the longer you hold), tap for 2 cm.
            floorUpButton = UIFactory.Button(root, "Floor ↑", null, false, Theme.White, 24);
            UIFactory.Place((RectTransform)floorUpButton.transform, new Vector2(1, 0.5f), new Vector2(170, 84), new Vector2(-32, 140));
            HoldRepeat.Attach(floorUpButton, d => NudgeFloor(d), FloorTapStep, FloorMinSpeed, FloorMaxSpeed);

            var heightBadge = UIFactory.Badge(root, "+0.00 m", Theme.Violet, 24, -2f);
            UIFactory.Place(heightBadge, new Vector2(1, 0.5f), new Vector2(150, 50), new Vector2(-42, 54));
            heightLabel = heightBadge.GetComponentInChildren<Text>();

            floorDownButton = UIFactory.Button(root, "Floor ↓", null, false, Theme.White, 24);
            UIFactory.Place((RectTransform)floorDownButton.transform, new Vector2(1, 0.5f), new Vector2(170, 84), new Vector2(-32, -32));
            HoldRepeat.Attach(floorDownButton, d => NudgeFloor(-d), FloorTapStep, FloorMinSpeed, FloorMaxSpeed);

            snapButton = UIFactory.Button(root, "Snap", SnapToGround, false, Theme.Yellow, 24);
            UIFactory.Place((RectTransform)snapButton.transform, new Vector2(1, 0.5f), new Vector2(170, 84), new Vector2(-32, -136));

            var measureBadge = UIFactory.Badge(root, "", Theme.Yellow, 30, 1.5f);
            UIFactory.Place(measureBadge, new Vector2(0.5f, 0), new Vector2(560, 70), new Vector2(0, 170));
            measureLabel = measureBadge.GetComponentInChildren<Text>();
            measureBadge.gameObject.SetActive(false);

            RefreshHud();
        }

        void RefreshHud()
        {
            if (status == null) return;
            bool placing = stage == Stage.Scanning || stage == Stage.Placing;
            bool placed = stage == Stage.Placed;

            placeButton.gameObject.SetActive(placing && !measuring);
            placeButton.interactable = true;
            recalibrateButton.gameObject.SetActive(placed || (placing && !simulated));
            // Placing: Recalibrate sits top-right (the bottom-left slot is "Use estimated floor").
            if (placed) UIFactory.Place((RectTransform)recalibrateButton.transform, new Vector2(0.5f, 0), new Vector2(226, 96), new Vector2(-242, 40));
            else UIFactory.Place((RectTransform)recalibrateButton.transform, new Vector2(1, 1), new Vector2(220, 80), new Vector2(-32, -222));
            adjustButton.gameObject.SetActive(placed && !simulated && !measuring);
            UIFactory.SetButtonLabel(adjustButton, adjusting ? "Lock" : "Adjust");
            adjustButton.targetGraphic.color = adjusting ? Theme.Yellow : Theme.White;
            bool tableScaleTools = !lifeSize && (placing || placed) && !measuring;
            scaleUpButton.gameObject.SetActive(tableScaleTools);
            scaleDownButton.gameObject.SetActive(tableScaleTools);
            scaleButton.interactable = !lifeSize;
            trackingBadge.gameObject.SetActive(!simulated);
            opacityButton.gameObject.SetActive(placed && !measuring);
            occlusionButton.gameObject.SetActive(placed && !simulated && !measuring);
            measureButton.gameObject.SetActive(placing || placed);
            UIFactory.SetButtonLabel(measureButton, measuring ? "Done" : "Measure");
            measureButton.targetGraphic.color = measuring ? Theme.Yellow : Theme.White;
            bool floorTrim = placed || (placing && useEstimatedFloor);
            floorUpButton.gameObject.SetActive(floorTrim);
            floorDownButton.gameObject.SetActive(floorTrim);
            heightLabel.transform.parent.gameObject.SetActive(placed);
            snapButton.gameObject.SetActive(placed);
            modeButton.gameObject.SetActive(placing || placed);
            scaleButton.gameObject.SetActive(placing || placed);

            UIFactory.SetButtonLabel(modeButton, lifeSize ? "Life-size" : "Tabletop");
            UIFactory.SetButtonLabel(opacityButton, $"Opacity {Mathf.RoundToInt(OpacitySteps[opacityStep] * 100)}%");
            UIFactory.SetButtonLabel(occlusionButton, occlusionOn ? "Occlusion: On" : "Occlusion: Off");
            scaleLabel.text = lifeSize ? "1:1" : $"1:{Mathf.Max(1, Mathf.RoundToInt(1f / (tableScale * userScale)))}";

            if (measuring) return;
            switch (stage)
            {
                case Stage.Scanning:
                    SetStatus(simulated ? "Look down at the ground" : lifeSize
                        ? "Move the phone slowly over the ground, or just tap where the model should go"
                        : "Tap the spot on the table where the model should go");
                    break;
                case Stage.Placing:
                    SetStatus(lifeSize
                        ? "Tap the spot where the park entrance should be (or aim the ring and tap PLACE)"
                        : "Tap the spot on the table where the model should go");
                    break;
                case Stage.Placed:
                    if (adjusting && !simulated)
                        SetStatus(lifeSize
                            ? "Adjusting: drag to move, twist with two fingers to rotate. Tap LOCK when done"
                            : "Adjusting: tap a new spot, drag to move, pinch to resize, twist to rotate. LOCK when done");
                    else
                        SetStatus(lifeSize
                            ? "Locked in place. Walk around to explore (ADJUST to move it)"
                            : "Locked on the table. Walk around it (ADJUST to move, SCALE to resize)");
                    break;
            }
        }

        void SetStatus(string text)
        {
            if (status != null) status.text = text;
        }

        void ShowToast(string text)
        {
            var bg = UIFactory.Card(status.canvas.transform, Theme.Ink, Vector2.zero, "Toast");
            UIFactory.Place(bg, new Vector2(0.5f, 0), new Vector2(1300, 76), new Vector2(0, 260));
            UIFactory.Tilt(bg, 0.8f);
            bg.GetComponent<Image>().raycastTarget = false;
            var t = UIFactory.Label(bg, text, 24, TextAnchor.MiddleCenter, Theme.Cream);
            UIFactory.Stretch(t.rectTransform, 10);
            Destroy(bg.gameObject, 8f);
        }

        void OnDestroy()
        {
            QualitySettings.shadowDistance = savedShadowDistance;
            model?.Dispose();
            if (planeMaterial != null) Destroy(planeMaterial);
            if (XRBootstrap.IsARActive && App.Instance != null) App.Instance.RunNextFrame(XRBootstrap.StopAR);
            if (rig != null) rig.gameObject.SetActive(true);
        }
    }
}
