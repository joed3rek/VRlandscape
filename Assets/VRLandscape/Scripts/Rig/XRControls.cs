using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace VRLandscape
{
    // Quest controls:
    //  Left stick  = walk (click the stick to run)     Right stick = snap turn 30 degrees
    //  Trigger     = select (laser from either hand)    Menu / B / Y = back to the park menu
    public class XRControls : MonoBehaviour
    {
        public System.Action OnBackPressed;
        public System.Action UserInput;          // sticks moved (View Sync: start roaming)
        public System.Action OnPresenterView;    // A button (View Sync: back to the presenter's view)

        PlayerRig rig;
        Transform rightHand, leftHand;
        LaserPointer rightLaser, leftLaser;
        InputAction move, turn, run, back, presenterView;
        bool turnLatched;

        void Awake()
        {
            rig = GetComponent<PlayerRig>();

            var hmd = rig.Cam.gameObject.AddComponent<TrackedPoseDriver>();
            hmd.positionInput = new InputActionProperty(Vector3Action("<XRHMD>/centerEyePosition"));
            hmd.rotationInput = new InputActionProperty(QuaternionAction("<XRHMD>/centerEyeRotation"));

            rightHand = CreateHand("Right Hand", "RightHand");
            leftHand = CreateHand("Left Hand", "LeftHand");

            rightLaser = LaserPointer.Create(rightHand, ButtonAction("<XRController>{RightHand}/{TriggerButton}"));
            leftLaser = LaserPointer.Create(leftHand, ButtonAction("<XRController>{LeftHand}/{TriggerButton}"));
            leftLaser.Visible = false; // shows up when the left trigger is used
            rightLaser.OnUsed = () => { rightLaser.Visible = true; leftLaser.Visible = false; };
            leftLaser.OnUsed = () => { leftLaser.Visible = true; rightLaser.Visible = false; };

            move = new InputAction("Move", InputActionType.Value, "<XRController>{LeftHand}/{Primary2DAxis}", expectedControlType: "Vector2");
            turn = new InputAction("Turn", InputActionType.Value, "<XRController>{RightHand}/{Primary2DAxis}", expectedControlType: "Vector2");
            run = ButtonAction("<XRController>{LeftHand}/{Primary2DAxisClick}");
            back = ButtonAction("<XRController>{LeftHand}/{MenuButton}");
            back.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
            back.AddBinding("<XRController>{LeftHand}/{SecondaryButton}");
            presenterView = ButtonAction("<XRController>{RightHand}/{PrimaryButton}");
            move.Enable(); turn.Enable(); run.Enable(); back.Enable();
        }

        Transform CreateHand(string name, string usage)
        {
            var hand = new GameObject(name).transform;
            hand.SetParent(rig.CameraOffset, false);
            var driver = hand.gameObject.AddComponent<TrackedPoseDriver>();
            driver.positionInput = new InputActionProperty(Vector3Action($"<XRController>{{{usage}}}/devicePosition"));
            driver.rotationInput = new InputActionProperty(QuaternionAction($"<XRController>{{{usage}}}/deviceRotation"));
            Visuals.Primitive(PrimitiveType.Cube, hand, new Vector3(0, 0, -0.03f), new Vector3(0.04f, 0.03f, 0.12f),
                Visuals.Lit(new Color(0.15f, 0.15f, 0.17f)), collider: false);
            return hand;
        }

        void Update()
        {
            var stick = move.ReadValue<Vector2>();
            if (stick.magnitude < 0.15f) stick = Vector2.zero;
            if (stick != Vector2.zero || Mathf.Abs(turn.ReadValue<Vector2>().x) > 0.7f) UserInput?.Invoke();
            if (presenterView.WasPressedThisFrame()) OnPresenterView?.Invoke();
            rig.Move(stick, run.IsPressed());

            float x = turn.ReadValue<Vector2>().x;
            if (!turnLatched && Mathf.Abs(x) > 0.7f)
            {
                turnLatched = true;
                transform.RotateAround(rig.Cam.transform.position, Vector3.up, Mathf.Sign(x) * 30f);
            }
            else if (Mathf.Abs(x) < 0.3f) turnLatched = false;

            if (back.WasPressedThisFrame()) OnBackPressed?.Invoke();
        }

        void OnDestroy()
        {
            move?.Dispose(); turn?.Dispose(); run?.Dispose(); back?.Dispose(); presenterView?.Dispose();
        }

        static InputAction Vector3Action(string binding)
        {
            var a = new InputAction(type: InputActionType.Value, binding: binding, expectedControlType: "Vector3");
            a.Enable();
            return a;
        }

        static InputAction QuaternionAction(string binding)
        {
            var a = new InputAction(type: InputActionType.Value, binding: binding, expectedControlType: "Quaternion");
            a.Enable();
            return a;
        }

        static InputAction ButtonAction(string binding)
        {
            var a = new InputAction(type: InputActionType.Button, binding: binding);
            a.Enable();
            return a;
        }
    }

    // Ray from a controller: highlights PointerTargets and activates them on trigger.
    public class LaserPointer : MonoBehaviour
    {
        public System.Action OnUsed;
        public bool Visible
        {
            get => line.enabled;
            set
            {
                line.enabled = value;
                if (!value && hovered != null) { hovered.SetHover(false); hovered = null; }
            }
        }

        const float MaxDistance = 40f;
        InputAction select;
        LineRenderer line;
        PointerTarget hovered;

        public static LaserPointer Create(Transform hand, InputAction select)
        {
            var laser = hand.gameObject.AddComponent<LaserPointer>();
            laser.select = select;
            laser.line = hand.gameObject.AddComponent<LineRenderer>();
            laser.line.useWorldSpace = true;
            laser.line.positionCount = 2;
            laser.line.startWidth = 0.006f;
            laser.line.endWidth = 0.002f;
            laser.line.sharedMaterial = Visuals.Unlit(new Color(0.4f, 0.85f, 1f, 0.7f));
            laser.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return laser;
        }

        void Update()
        {
            var origin = transform.position;
            var dir = transform.forward;
            PointerTarget target = null;
            float length = MaxDistance;
            if (Physics.Raycast(origin, dir, out var hit, MaxDistance, ~0, QueryTriggerInteraction.Collide))
            {
                length = hit.distance;
                target = hit.collider.GetComponentInParent<PointerTarget>();
            }

            if (Visible)
            {
                line.SetPosition(0, origin);
                line.SetPosition(1, origin + dir * length);
            }

            if (target != hovered && Visible)
            {
                if (hovered != null) hovered.SetHover(false);
                hovered = target;
                if (hovered != null) hovered.SetHover(true);
            }

            if (select.WasPressedThisFrame())
            {
                if (!Visible) { OnUsed?.Invoke(); return; }
                OnUsed?.Invoke();
                if (target != null) target.Activate();
            }
        }

        void OnDisable()
        {
            if (hovered != null) hovered.SetHover(false);
            hovered = null;
        }
    }
}
