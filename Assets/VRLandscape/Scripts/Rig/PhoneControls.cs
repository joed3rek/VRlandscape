using UnityEngine;
using UnityEngine.InputSystem;

namespace VRLandscape
{
    // Phone / desktop look and move.
    //  - Gyroscope: point the phone around to look (magic-window style).
    //  - Drag on the screen: turns left/right (and up/down when the gyro is off).
    //  - On-screen joystick (PhoneHud) or WASD/arrows + Shift in the editor: walk.
    public class PhoneControls : MonoBehaviour
    {
        public Vector2 JoystickInput { get; set; }
        public Vector2 LookStickInput { get; set; }   // right-hand camera joystick, used when the gyro is off
        public float LookStickSpeed = 150f;            // degrees per second at full deflection

        // View Sync: while a viewer follows a presenter the camera is driven remotely.
        // Any local touch/stick/key raises UserInput so the app can switch the viewer to roaming.
        public bool ExternallyDriven { get; set; }
        public event System.Action UserInput;
        public bool GyroAvailable => AttitudeSensor.current != null;
        public bool GyroEnabled { get; private set; }

        PlayerRig rig;
        float yaw, pitch;          // manual look (gyro off)
        float gyroYawOffset;       // extra heading applied on top of the gyro (drag + recenter)
        Quaternion lastGyro = Quaternion.identity;
        bool hasGyroSample;

        void Awake()
        {
            rig = GetComponent<PlayerRig>();
        }

        void Start()
        {
#if !UNITY_EDITOR
            SetGyro(GyroAvailable);
#endif
        }

        public void SetGyro(bool on)
        {
            if (on && !GyroAvailable) on = false;
            float currentYaw = rig.Cam.transform.eulerAngles.y;
            float currentPitch = NormalizeAngle(rig.Cam.transform.eulerAngles.x);

            if (on)
            {
                InputSystem.EnableDevice(AttitudeSensor.current);
                hasGyroSample = false;
                GyroEnabled = true;
                pendingYaw = currentYaw;
            }
            else
            {
                if (AttitudeSensor.current != null && AttitudeSensor.current.enabled)
                    InputSystem.DisableDevice(AttitudeSensor.current);
                GyroEnabled = false;
                yaw = currentYaw;
                pitch = Mathf.Clamp(currentPitch, -85f, 85f);
            }
        }

        float? pendingYaw;

        public void SetYaw(float newYaw)
        {
            if (GyroEnabled)
            {
                if (hasGyroSample) gyroYawOffset = newYaw - lastGyro.eulerAngles.y;
                else pendingYaw = newYaw;
            }
            else
            {
                yaw = newYaw;
                pitch = 0;
            }
            ApplyRotation();
        }

        // Screen-drag look, delta in pixels.
        public void Look(Vector2 deltaPixels)
        {
            UserInput?.Invoke();
            float degreesPerPixel = 140f / Mathf.Max(1, Screen.width);
            if (GyroEnabled)
            {
                gyroYawOffset += deltaPixels.x * degreesPerPixel;
            }
            else
            {
                yaw += deltaPixels.x * degreesPerPixel;
                pitch = Mathf.Clamp(pitch - deltaPixels.y * degreesPerPixel, -85f, 85f);
            }
        }

        // Continue from wherever the camera was pointed (e.g. by the presenter) without a jump.
        public void SyncLookFromCamera()
        {
            var e = rig.Cam.transform.localEulerAngles;
            yaw = e.y;
            pitch = Mathf.Clamp(NormalizeAngle(e.x), -85f, 85f);
            if (GyroEnabled && hasGyroSample) gyroYawOffset = e.y - lastGyro.eulerAngles.y;
        }

        bool AnyLocalInput()
        {
            if (JoystickInput.sqrMagnitude > 0.01f || LookStickInput.sqrMagnitude > 0.01f) return true;
            var kb = Keyboard.current;
            return kb != null && (kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed ||
                                  kb.upArrowKey.isPressed || kb.downArrowKey.isPressed || kb.leftArrowKey.isPressed ||
                                  kb.rightArrowKey.isPressed || kb.qKey.isPressed || kb.eKey.isPressed);
        }

        void Update()
        {
            if (ExternallyDriven)
            {
                ReadGyro();   // keep the gyro fresh so roaming starts smoothly
                if (AnyLocalInput()) UserInput?.Invoke();
                if (ExternallyDriven) return;
            }
            if (!GyroEnabled && LookStickInput != Vector2.zero)
            {
                // Squared response: fine aiming near the centre, fast turns at the edge.
                var v = new Vector2(LookStickInput.x * Mathf.Abs(LookStickInput.x), LookStickInput.y * Mathf.Abs(LookStickInput.y));
                yaw += v.x * LookStickSpeed * Time.deltaTime;
                pitch = Mathf.Clamp(pitch - v.y * LookStickSpeed * 0.6f * Time.deltaTime, -85f, 85f);
            }
            ReadGyro();
            ApplyRotation();

            var move = JoystickInput;
            bool run = JoystickInput.magnitude > 0.95f;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1;
                if (kb.leftShiftKey.isPressed) run = true;
                if (kb.qKey.isPressed) Look(new Vector2(-400f * Time.deltaTime, 0));
                if (kb.eKey.isPressed) Look(new Vector2(400f * Time.deltaTime, 0));
            }
            rig.Move(Vector2.ClampMagnitude(move, 1f), run);
        }

        void ReadGyro()
        {
            if (!GyroEnabled || AttitudeSensor.current == null) return;
            var q = AttitudeSensor.current.attitude.ReadValue();
            if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 0.5f) return; // no sample yet

            lastGyro = DeviceToUnity(q);
            if (!hasGyroSample)
            {
                hasGyroSample = true;
                if (pendingYaw.HasValue) gyroYawOffset = pendingYaw.Value - lastGyro.eulerAngles.y;
                pendingYaw = null;
            }
        }

        void ApplyRotation()
        {
            if (GyroEnabled && hasGyroSample)
                rig.Cam.transform.localRotation = Quaternion.Euler(0, gyroYawOffset, 0) * lastGyro;
            else if (!GyroEnabled)
                rig.Cam.transform.localRotation = Quaternion.Euler(pitch, yaw, 0);
        }

        // Android rotation vector: right-handed, rotates device axes into an East-North-Up world.
        // Device axes: x = right, y = top, z = out of the screen (in the device's natural portrait orientation).
        public static Quaternion DeviceToUnity(Quaternion q)
        {
            Vector3 screenUp, screenForward = new Vector3(0, 0, -1); // camera looks out the back of the phone
            if (InputSystem.settings.compensateForScreenOrientation)
            {
                screenUp = new Vector3(0, 1, 0); // already expressed in screen axes
            }
            else
            {
                switch (Screen.orientation)
                {
                    case ScreenOrientation.LandscapeLeft: screenUp = new Vector3(1, 0, 0); break;
                    case ScreenOrientation.LandscapeRight: screenUp = new Vector3(-1, 0, 0); break;
                    case ScreenOrientation.PortraitUpsideDown: screenUp = new Vector3(0, -1, 0); break;
                    default: screenUp = new Vector3(0, 1, 0); break;
                }
            }
            Vector3 fwdEnu = q * screenForward;
            Vector3 upEnu = q * screenUp;
            // ENU (x east, y north, z up) -> Unity (x east, y up, z north)
            var fwd = new Vector3(fwdEnu.x, fwdEnu.z, fwdEnu.y);
            var up = new Vector3(upEnu.x, upEnu.z, upEnu.y);
            return Quaternion.LookRotation(fwd, up);
        }

        static float NormalizeAngle(float a) => a > 180f ? a - 360f : a;
    }
}
