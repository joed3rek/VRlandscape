using UnityEngine;

namespace VRLandscape
{
    // The viewer's body: a CharacterController with the camera on top.
    // Phone: camera sits at eye height and PhoneControls rotates it.
    // Quest: the floor-origin headset pose drives the camera, XRControls handles sticks and laser.
    public class PlayerRig : MonoBehaviour
    {
        public const float EyeHeight = 1.6f;

        public Camera Cam { get; private set; }
        public Transform CameraOffset { get; private set; }
        public CharacterController Body { get; private set; }
        public PhoneControls Phone { get; private set; }
        public XRControls XR { get; private set; }
        public bool IsXR => XR != null;

        public bool MovementEnabled { get; set; }
        public float WalkSpeed = 1.6f;
        public float RunSpeed = 4f;

        Vector3 respawnPosition;
        float respawnYaw;
        float killY = -1000f;
        float verticalVelocity;

        public static PlayerRig Create(bool xr)
        {
            var go = new GameObject("Player Rig");
            DontDestroyOnLoad(go);
            var rig = go.AddComponent<PlayerRig>();

            rig.Body = go.AddComponent<CharacterController>();
            rig.Body.height = 1.8f;
            rig.Body.radius = 0.3f;
            rig.Body.center = new Vector3(0, 0.9f, 0);
            rig.Body.stepOffset = 0.4f;
            rig.Body.slopeLimit = 50f;
            rig.Body.skinWidth = 0.03f;

            rig.CameraOffset = new GameObject("Camera Offset").transform;
            rig.CameraOffset.SetParent(go.transform, false);
            rig.CameraOffset.localPosition = xr ? Vector3.zero : new Vector3(0, EyeHeight, 0);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(rig.CameraOffset, false);
            rig.Cam = camGo.AddComponent<Camera>();
            rig.Cam.nearClipPlane = 0.05f;
            rig.Cam.farClipPlane = 2000f;
            camGo.AddComponent<AudioListener>();
            if (RenderSettings.skybox == null)
            {
                rig.Cam.clearFlags = CameraClearFlags.SolidColor;
                rig.Cam.backgroundColor = new Color(0.62f, 0.78f, 0.92f);
            }

            if (xr) rig.XR = go.AddComponent<XRControls>();
            else rig.Phone = go.AddComponent<PhoneControls>();
            return rig;
        }

        public float HeadYaw => Cam.transform.eulerAngles.y;

        public void Teleport(Vector3 feetPosition, float yaw)
        {
            Body.enabled = false;
            transform.position = feetPosition;
            verticalVelocity = 0;
            if (IsXR)
            {
                // Turn the play space so the current head direction faces the requested yaw.
                float headLocalYaw = Cam.transform.localEulerAngles.y;
                transform.rotation = Quaternion.Euler(0, yaw - headLocalYaw, 0);
                // Keep the head (not the play-space centre) over the requested spot.
                var headOffset = Cam.transform.position - transform.position;
                headOffset.y = 0;
                transform.position -= headOffset;
            }
            else
            {
                transform.rotation = Quaternion.identity;
                Phone.SetYaw(yaw);
            }
            Body.enabled = true;
        }

        public void SetRespawn(Vector3 position, float yaw, float minY)
        {
            respawnPosition = position;
            respawnYaw = yaw;
            killY = minY;
        }

        // direction: x = strafe, y = forward, relative to where the user looks.
        public void Move(Vector2 direction, bool run)
        {
            if (!MovementEnabled || !Body.enabled) return;

            var forward = Cam.transform.forward; forward.y = 0; forward.Normalize();
            var right = Cam.transform.right; right.y = 0; right.Normalize();
            var horizontal = (forward * direction.y + right * direction.x);
            if (horizontal.sqrMagnitude > 1f) horizontal.Normalize();
            horizontal *= run ? RunSpeed : WalkSpeed;

            verticalVelocity = Body.isGrounded ? -1f : verticalVelocity - 9.81f * Time.deltaTime;
            Body.Move((horizontal + Vector3.up * verticalVelocity) * Time.deltaTime);

            if (transform.position.y < killY) Teleport(respawnPosition, respawnYaw);
        }
    }
}
