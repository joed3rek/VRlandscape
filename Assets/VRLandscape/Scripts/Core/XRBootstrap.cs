using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace VRLandscape
{
    // One APK serves Quest and phones. "Initialize XR on Startup" is off and loaders are started by code:
    //  - OpenXR at launch, only on headsets (VR mode).
    //  - ARCore on phones, only while AR Site Walk is open.
    public static class XRBootstrap
    {
        public const string EditorPrefKey = "VRLandscape.TryXRInEditor";

        public static bool IsXRActive { get; private set; }
        public static bool IsARActive { get; private set; }

        static List<XRLoader> registeredLoaders;

        static XRManagerSettings Manager => XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;

        public static IEnumerator TryStart()
        {
            if (!ShouldTryVR()) yield break;
            var manager = Manager;
            if (manager == null) yield break;

            if (manager.activeLoader == null)
            {
                SelectLoaders(manager, "OpenXR");
                yield return manager.InitializeLoader();
            }
            if (manager.activeLoader == null)
            {
                Debug.Log("[VRLandscape] No XR runtime, running in phone mode.");
                yield break;
            }

            manager.StartSubsystems();
            IsXRActive = true;

            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var input in inputs) input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
        }

        public static IEnumerator StartAR()
        {
            var manager = Manager;
            if (manager == null || IsXRActive || IsARActive) yield break;
            if (!SelectLoaders(manager, "ARCore")) yield break;

            yield return manager.InitializeLoader();
            if (manager.activeLoader == null)
            {
                Debug.Log("[VRLandscape] ARCore loader failed to start.");
                yield break;
            }
            manager.StartSubsystems();
            IsARActive = true;
        }

        public static void StopAR()
        {
            if (!IsARActive) return;
            var manager = Manager;
            if (manager != null && manager.activeLoader != null)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
            IsARActive = false;
        }

        public static void Stop()
        {
            StopAR();
            if (!IsXRActive) return;
            var manager = Manager;
            if (manager != null && manager.activeLoader != null)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
            IsXRActive = false;
        }

        // Narrows the manager to loaders whose type name contains `match`.
        // Skipped in the editor, where changing the list would modify the settings asset.
        static bool SelectLoaders(XRManagerSettings manager, string match)
        {
            if (registeredLoaders == null) registeredLoaders = new List<XRLoader>(manager.activeLoaders);
            var picked = registeredLoaders.FindAll(l => l != null && l.GetType().Name.Contains(match));
            if (picked.Count == 0)
            {
                Debug.LogWarning($"[VRLandscape] No {match} loader enabled in XR Plug-in Management.");
                return false;
            }
#if UNITY_EDITOR
            return true;
#else
            return manager.TrySetLoaders(picked);
#endif
        }

        static bool ShouldTryVR()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false);
#elif UNITY_ANDROID
            string device = (SystemInfo.deviceModel + " " + SystemInfo.deviceName).ToLowerInvariant();
            return device.Contains("quest") || device.Contains("oculus") || device.Contains("pico");
#else
            return true;
#endif
        }
    }
}
