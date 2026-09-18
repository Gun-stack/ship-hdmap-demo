using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// The virtual viewpoint (spec §5.3). Click the deck in Probe mode and the camera stands there at eye
    /// height, showing exactly what LandmarkSensor.VisibleFrom says a sensor there would see -- same call the
    /// drive makes, so the editor cannot promise a view the drive will not deliver.
    ///
    /// Never persisted: it is a question the operator is asking, not a fact about the ship.
    public class ProbeView : MonoBehaviour
    {
        public LandmarkSensor sensor; public SensorView view; public OrbitCamera orbit; public Transform root;
        public float eyeHeight = 1.2f, turnDegPerSec = 60f;
        public bool Active { get; private set; }
        Pose2D _pose; double _zSurface;

        public void PlaceAt(Vector3 worldPoint, double zSurface)
        {
            var local = root ? root.InverseTransformPoint(worldPoint) : worldPoint;
            var (x, y, _) = ShipFrame.ToShip(local);
            _pose = new Pose2D { x = x, y = y, psiRad = _pose.psiRad };   // keep the heading across re-clicks
            _zSurface = zSurface; Active = true;
            Aim();
        }

        public void Clear() { Active = false; if (view) view.Hide(); }

        void Update()
        {
            if (!Active) return;
            float turn = (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f);
            if (turn != 0f) { _pose.psiRad = ShipFrame.WrapRad(_pose.psiRad + turn * turnDegPerSec * Mathf.Deg2Rad * Time.unscaledDeltaTime); Aim(); }
        }

        /// Recompute the overlay and park the camera at the probe's eye.
        public void Aim()
        {
            if (sensor == null || view == null) return;
            var runtime = GetComponent<MapRuntime>();
            Vector3 eyeLocal = ShipFrame.ToUnity(_pose.x, _pose.y, _zSurface + eyeHeight);
            Vector3 eyeWorld = root ? root.TransformPoint(eyeLocal) : eyeLocal;
            var why = sensor.VisibleFrom(_pose, eyeWorld, runtime.MapRefs, id => runtime.MarkerPos(id));
            view.Show(_pose, _zSurface, why, id => runtime.MarkerOf(id), sensor.fovDeg, sensor.maxDist);
            if (orbit)
            {
                orbit.mode = CamMode.Driver; orbit.driverTarget = null;
                // Same trap as ApplyDriver: a yaw rotation puts the HEADING on +X, but the camera looks down
                // +Z. HeadingVector is the codebase's own ship-heading -> Unity-direction, and it goes through
                // `root` because the probe's heading is a ship heading and the hull is trimmed and heeled --
                // the eye position right above takes the same trip.
                Vector3 look = ShipFrame.HeadingVector(_pose.psiRad * 180 / Math.PI);
                orbit.transform.SetPositionAndRotation(eyeWorld,
                    root ? Quaternion.LookRotation(root.TransformDirection(look), root.up) : Quaternion.LookRotation(look, Vector3.up));
            }
        }
    }
}
