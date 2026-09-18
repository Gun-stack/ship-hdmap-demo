using UnityEngine;

namespace ShipHdMap
{
    /// Orbit is the editor's camera, Fly walks the deck, Driver rides the vehicle at sensor eye height.
    public enum CamMode { Orbit, Fly, Driver }

    /// Orbit camera on the Main Camera: right-drag orbits, middle-drag pans, wheel zooms. Left button is left to LandmarkPlacer.
    /// Set `follow` to track a transform (drive mode); Focus() jumps to a point and stops following.
    public class OrbitCamera : MonoBehaviour
    {
        public Vector3 target = new Vector3(60, 10, 0);
        public float yawDeg = -20f, pitchDeg = 25f, distance = 70f;
        public float minDistance = 3f, maxDistance = 300f, orbitSpeed = 3f, panSpeed = 0.02f;
        public Transform follow; public float followLerp = 4f;

        public CamMode mode = CamMode.Orbit;
        public Transform driverTarget; public float driverEyeM = 1.2f, flySpeed = 12f, flyBoost = 4f;

        /// Camera position/rotation for a target, yaw about world up (deg), pitch above the horizon (deg) and distance.
        public static (Vector3 pos, Quaternion rot) Pose(Vector3 target, float yawDeg, float pitchDeg, float distance)
        {
            var rot = Quaternion.Euler(pitchDeg, yawDeg, 0);
            return (target - rot * Vector3.forward * distance, rot);
        }

        /// Pure translation for one free-flight frame. `axes` is (right, worldUp, forward) in -1..1.
        /// Forward and right follow where the camera looks; up is world up, so Q/E always mean up and down.
        public static (Vector3 pos, Quaternion rot) FlyStep(Vector3 pos, Quaternion rot, Vector3 axes, float dt, float speed)
        {
            Vector3 d = rot * Vector3.forward * axes.z + rot * Vector3.right * axes.x + Vector3.up * axes.y;
            return (pos + d * (speed * dt), rot);
        }

        /// The driver's eye is the sensor's eye: same height, same heading (spec §5.2).
        ///
        /// A null `driverTarget` is a CONTRACT, not a missing case: it means "Driver mode, but somebody else
        /// placed this camera -- leave it exactly where it is". ProbeView depends on it to hold the camera at
        /// the virtual viewpoint it just parked there. Do NOT turn this into an orbit fallback: the only thing
        /// allowed to take the camera back is MapRuntime.ReleaseProbe, which returns the MODE to Orbit.
        public void ApplyDriver()
        {
            if (!driverTarget) return;
            transform.SetPositionAndRotation(driverTarget.position + Vector3.up * driverEyeM, driverTarget.rotation);
        }

        /// The orbit pitch floor, applied on EVERY orbit frame rather than only inside the right-drag guard.
        /// Fly clamps to +-89 and both Focus and AdoptCurrentPose import whatever fly left behind, so an orbit
        /// frame can begin below this floor -- and used to stay there, underneath the deck, until the first
        /// right-drag snapped it up some 84 degrees in one step.
        public void ClampOrbitPitch() => pitchDeg = Mathf.Clamp(pitchDeg, -5f, 89f);

        public void Focus(Vector3 p, float dist) { mode = CamMode.Orbit; follow = null; target = p; distance = Mathf.Clamp(dist, minDistance, maxDistance); Apply(); }

        /// Adopts the camera's current transform so the first frame does not jump: yaw/pitch from its rotation, target `distance` ahead along its forward.
        public void AdoptCurrentPose()
        {
            var e = transform.rotation.eulerAngles;
            yawDeg = e.y; pitchDeg = e.x > 180f ? e.x - 360f : e.x;
            target = transform.position + transform.forward * distance;
        }

        void LateUpdate()
        {
            if (mode == CamMode.Driver) { ApplyDriver(); return; }
            if (mode == CamMode.Fly) { StepFly(); return; }
            if (Input.GetMouseButton(1)) { yawDeg += Input.GetAxis("Mouse X") * orbitSpeed; pitchDeg -= Input.GetAxis("Mouse Y") * orbitSpeed; }
            ClampOrbitPitch();
            if (Input.GetMouseButton(2))
            {
                var flat = Quaternion.Euler(0, yawDeg, 0);
                target -= flat * new Vector3(Input.GetAxis("Mouse X"), 0, Input.GetAxis("Mouse Y")) * (panSpeed * distance);
                follow = null;
            }
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f) distance = Mathf.Clamp(distance * Mathf.Pow(0.9f, wheel), minDistance, maxDistance);
            if (follow) target = Vector3.Lerp(target, follow.position, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
            Apply();
        }

        /// Right-drag looks around, WASD/arrows move, Q/E rise and fall, Shift boosts.
        /// Unity only receives these when its canvas has focus (WebGLInput.captureAllKeyboardInput = false),
        /// which is exactly the condition the web's shortcut handler steps aside for.
        void StepFly()
        {
            if (Input.GetMouseButton(1))
            {
                yawDeg += Input.GetAxis("Mouse X") * orbitSpeed;
                pitchDeg = Mathf.Clamp(pitchDeg - Input.GetAxis("Mouse Y") * orbitSpeed, -89f, 89f);
            }
            var rot = Quaternion.Euler(pitchDeg, yawDeg, 0);
            float x = Axis(KeyCode.D, KeyCode.A) + Axis(KeyCode.RightArrow, KeyCode.LeftArrow);
            float z = Axis(KeyCode.W, KeyCode.S) + Axis(KeyCode.UpArrow, KeyCode.DownArrow);
            float y = Axis(KeyCode.E, KeyCode.Q);
            float speed = flySpeed * (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? flyBoost : 1f);
            var (p, r) = FlyStep(transform.position, rot, new Vector3(Mathf.Clamp(x, -1, 1), Mathf.Clamp(y, -1, 1), Mathf.Clamp(z, -1, 1)), Time.unscaledDeltaTime, speed);
            transform.SetPositionAndRotation(p, r);
            // leaving fly mode should not snap back to wherever the orbit target was
            target = p + r * Vector3.forward * distance;
        }

        static float Axis(KeyCode plus, KeyCode minus) => (Input.GetKey(plus) ? 1f : 0f) - (Input.GetKey(minus) ? 1f : 0f);

        void Apply() { var (p, r) = Pose(target, yawDeg, pitchDeg, distance); transform.SetPositionAndRotation(p, r); }
    }
}
