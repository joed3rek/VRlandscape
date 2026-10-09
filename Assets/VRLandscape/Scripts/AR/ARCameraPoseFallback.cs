using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;

namespace VRLandscape
{
    // Safety net for the AR camera pose. Normally the Input System TrackedPoseDriver moves the camera.
    // If ARCore is tracking but the camera hasn't moved at all after a couple of seconds (pose not reaching
    // the Input System on this device), this takes over and reads the pose from Unity's XR input devices.
    // Without a moving camera the model sticks to the screen instead of staying on the ground.
    public class ARCameraPoseFallback : MonoBehaviour
    {
        public bool Active { get; private set; }

        const float GraceSeconds = 2f;
        float trackingSince = -1f;
        readonly List<InputDevice> devices = new List<InputDevice>();

        void OnEnable() => Application.onBeforeRender += OnBeforeRender;
        void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

        void Update()
        {
            if (!Active) DetectStuckCamera();
            if (Active) Apply();
        }

        void OnBeforeRender()
        {
            if (Active) Apply();
        }

        void DetectStuckCamera()
        {
            if (ARSession.state != ARSessionState.SessionTracking) { trackingSince = -1f; return; }
            if (trackingSince < 0f) trackingSince = Time.time;
            if (Time.time - trackingSince < GraceSeconds) return;
            if (transform.localPosition != Vector3.zero || transform.localRotation != Quaternion.identity) { enabled = false; return; }

            Active = true;
            var driver = GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (driver != null) driver.enabled = false;
            Debug.LogWarning("[VRLandscape] AR camera pose wasn't updating; using the XR input device fallback.");
        }

        void Apply()
        {
            InputDevices.GetDevices(devices);
            foreach (var d in devices)
            {
                if ((d.characteristics & InputDeviceCharacteristics.TrackedDevice) == 0) continue;
                if (!d.TryGetFeatureValue(CommonUsages.centerEyePosition, out var position) &&
                    !d.TryGetFeatureValue(CommonUsages.devicePosition, out position)) continue;
                if (!d.TryGetFeatureValue(CommonUsages.centerEyeRotation, out var rotation) &&
                    !d.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation)) continue;
                transform.localPosition = position;
                transform.localRotation = rotation;
                return;
            }
        }
    }
}
