using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VRLandscape
{
    // Phone GPS position and compass heading for On-site mode.
    // GPS: Unity's LocationService (asks for the location permission).
    // Heading: Android's rotation-vector sensor (gyro + accelerometer + magnetometer), smoothed.
    public class GeoLocation : MonoBehaviour
    {
        public enum State { Starting, Running, Failed }

        public State Status { get; private set; } = State.Starting;
        public string Error { get; private set; }
        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public float Accuracy { get; private set; } = 999f;   // metres
        public bool HasHeading { get; private set; }
        public float Heading { get; private set; }             // degrees from north, direction the camera faces

        static GeoLocation instance;

        public static GeoLocation Ensure()
        {
            if (instance != null) return instance;
            var go = new GameObject("Geo Location");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GeoLocation>();
            return instance;
        }

        IEnumerator Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);
                float wait = 0;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation) && wait < 30f)
                {
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
#endif
            bool started = false;
            try
            {
                if (!Input.location.isEnabledByUser)
                {
                    Fail("Location is turned off. Turn on Location in the phone settings.");
                }
                else
                {
                    Input.location.Start(2f, 0.5f);
                    started = true;
                }
            }
            catch (Exception e)
            {
                Fail("GPS unavailable: " + e.Message);
            }
            if (!started) yield break;

            float timeout = 25f;
            while (Input.location.status == LocationServiceStatus.Initializing && timeout > 0)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (Input.location.status == LocationServiceStatus.Running) Status = State.Running;
            else Fail("Could not get a GPS fix. Go outdoors and try again.");
        }

        void Fail(string message)
        {
            Status = State.Failed;
            Error = message;
            Debug.LogWarning("[VRLandscape] " + message);
        }

        void Update()
        {
            if (Status == State.Running)
            {
                var d = Input.location.lastData;
                Latitude = d.latitude;
                Longitude = d.longitude;
                Accuracy = d.horizontalAccuracy > 0 ? d.horizontalAccuracy : 999f;
            }
            ReadHeading();
        }

        void ReadHeading()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var sensor = InputSystem.GetDevice<UnityEngine.InputSystem.Android.AndroidRotationVector>();
            if (sensor == null) return;
            if (!sensor.enabled) InputSystem.EnableDevice(sensor);
            var q = sensor.attitude.ReadValue();
            if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 0.5f) return;
            float raw = PhoneControls.DeviceToUnity(q).eulerAngles.y;
            // Circular low-pass so the arrow doesn't jitter.
            Heading = HasHeading ? Mathf.LerpAngle(Heading, raw, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime)) : raw;
            Heading = (Heading + 360f) % 360f;
            HasHeading = true;
#endif
        }

        void OnDestroy()
        {
            try { if (Status == State.Running) Input.location.Stop(); } catch { /* ignore */ }
            if (instance == this) instance = null;
        }

        // ------------------------------------------------------------ geodesy

        const double EarthRadius = 6371000.0;

        public static float Distance(double lat1, double lon1, double lat2, double lon2)
        {
            double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180;
            double dp = p2 - p1, dl = (lon2 - lon1) * Math.PI / 180;
            double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
            return (float)(EarthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
        }

        // Compass bearing (0 = north, clockwise) from point 1 to point 2.
        public static float Bearing(double lat1, double lon1, double lat2, double lon2)
        {
            double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180, dl = (lon2 - lon1) * Math.PI / 180;
            double y = Math.Sin(dl) * Math.Cos(p2);
            double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
            return (float)((Math.Atan2(y, x) * 180 / Math.PI + 360) % 360);
        }
    }
}
