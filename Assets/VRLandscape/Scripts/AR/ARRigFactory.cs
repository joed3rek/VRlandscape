using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;

namespace VRLandscape
{
    // Shared ARCore camera rig (session + XR Origin + camera with the live feed) for AR Site Walk and On-site mode.
    public static class ARRigFactory
    {
        public class Rig
        {
            public GameObject root;
            public ARSession session;
            public XROrigin origin;
            public Camera camera;
        }

        // Starts ARCore and builds the rig. `extras` adds mode-specific components (planes, occlusion...)
        // before the rig activates. `done` gets null when ARCore isn't available (editor, unsupported phone).
        public static IEnumerator Start(Transform parent, Action<GameObject, GameObject> extras, Action<string> status, Action<Rig> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return XRBootstrap.StartAR();
            if (XRBootstrap.IsARActive)
            {
                var rig = Create(parent, extras);
                yield return ARSession.CheckAvailability();
                if (ARSession.state == ARSessionState.NeedsInstall)
                {
                    status?.Invoke("Installing Google Play Services for AR...");
                    yield return ARSession.Install();
                }
                if (ARSession.state == ARSessionState.Unsupported || ARSession.state == ARSessionState.None ||
                    ARSession.state == ARSessionState.NeedsInstall)
                {
                    UnityEngine.Object.Destroy(rig.root);
                    yield return null;
                    XRBootstrap.StopAR();
                }
                else
                {
                    done(rig);
                    yield break;
                }
            }
#endif
            done(null);
            yield break;
        }

        static Rig Create(Transform parent, Action<GameObject, GameObject> extras)
        {
            var rig = new Rig { root = new GameObject("AR") };
            rig.root.transform.SetParent(parent, false);

            var sessionGo = new GameObject("AR Session");
            sessionGo.transform.SetParent(rig.root.transform, false);
            rig.session = sessionGo.AddComponent<ARSession>();
            sessionGo.AddComponent<ARInputManager>();

            var originGo = new GameObject("XR Origin");
            originGo.SetActive(false);
            originGo.transform.SetParent(rig.root.transform, false);
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);

            var camGo = new GameObject("AR Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(offset.transform, false);
            rig.camera = camGo.AddComponent<Camera>();
            rig.camera.clearFlags = CameraClearFlags.SolidColor;
            rig.camera.backgroundColor = Color.black;
            rig.camera.nearClipPlane = 0.05f;
            rig.camera.farClipPlane = 1000f;
            camGo.AddComponent<ARCameraManager>();
            camGo.AddComponent<ARCameraBackground>();
            // Same bindings as Unity's own "XR Origin (Mobile AR)": depending on the device, ARCore's pose arrives
            // as an XRHMD centre eye or as a HandheldARInputDevice, so listen to both.
            var pose = camGo.AddComponent<TrackedPoseDriver>();
            pose.positionInput = new InputActionProperty(EnabledAction("Vector3", "<XRHMD>/centerEyePosition", "<HandheldARInputDevice>/devicePosition"));
            pose.rotationInput = new InputActionProperty(EnabledAction("Quaternion", "<XRHMD>/centerEyeRotation", "<HandheldARInputDevice>/deviceRotation"));
            camGo.AddComponent<ARCameraPoseFallback>();

            rig.origin = originGo.AddComponent<XROrigin>();
            rig.origin.Camera = rig.camera;
            rig.origin.CameraFloorOffsetObject = offset;
            rig.origin.CameraYOffset = 0f;

            extras?.Invoke(originGo, camGo);
            originGo.SetActive(true);
            return rig;
        }

        static InputAction EnabledAction(string controlType, params string[] bindings)
        {
            var a = new InputAction(type: InputActionType.Value, binding: bindings[0], expectedControlType: controlType);
            for (int i = 1; i < bindings.Length; i++) a.AddBinding(bindings[i]);
            a.Enable();
            return a;
        }
    }
}
