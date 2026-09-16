using UnityEngine;

namespace ShipHdMap
{
    /// Orbit camera on the Main Camera: right-drag orbits, middle-drag pans, wheel zooms. Left button is left to LandmarkPlacer.
    /// Set `follow` to track a transform (drive mode); Focus() jumps to a point and stops following.
    public class OrbitCamera : MonoBehaviour
    {
        public Vector3 target = new Vector3(60, 10, 0);
        public float yawDeg = -20f, pitchDeg = 25f, distance = 70f;
        public float minDistance = 3f, maxDistance = 300f, orbitSpeed = 3f, panSpeed = 0.02f;
        public Transform follow; public float followLerp = 4f;
        public bool inputEnabled = true;

        /// Camera position/rotation for a target, yaw about world up (deg), pitch above the horizon (deg) and distance.
        public static (Vector3 pos, Quaternion rot) Pose(Vector3 target, float yawDeg, float pitchDeg, float distance)
        {
            var rot = Quaternion.Euler(pitchDeg, yawDeg, 0);
            return (target - rot * Vector3.forward * distance, rot);
        }

        public void Focus(Vector3 p, float dist) { follow = null; target = p; distance = Mathf.Clamp(dist, minDistance, maxDistance); Apply(); }

        void LateUpdate()
        {
            if (inputEnabled)
            {
                if (Input.GetMouseButton(1)) { yawDeg += Input.GetAxis("Mouse X") * orbitSpeed; pitchDeg = Mathf.Clamp(pitchDeg - Input.GetAxis("Mouse Y") * orbitSpeed, -5f, 89f); }
                if (Input.GetMouseButton(2))
                {
                    var flat = Quaternion.Euler(0, yawDeg, 0);
                    target -= flat * new Vector3(Input.GetAxis("Mouse X"), 0, Input.GetAxis("Mouse Y")) * (panSpeed * distance);
                    follow = null;
                }
                float wheel = Input.mouseScrollDelta.y;
                if (wheel != 0f) distance = Mathf.Clamp(distance * Mathf.Pow(0.9f, wheel), minDistance, maxDistance);
            }
            if (follow) target = Vector3.Lerp(target, follow.position, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
            Apply();
        }

        void Apply() { var (p, r) = Pose(target, yawDeg, pitchDeg, distance); transform.SetPositionAndRotation(p, r); }
    }
}
