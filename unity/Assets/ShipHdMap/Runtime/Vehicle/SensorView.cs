using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// Draws what the sensor can see, on the deck floor: the two field-of-view edges and the range arc,
    /// plus a green ring on every marker currently observed. Shared by the driver's-eye camera and the
    /// probe (spec §5.2, §5.3) so both read the same judgement out of LandmarkSensor.VisibleFrom.
    ///
    /// Why draw the cone and the arc and not the other two conditions: a marker that went dark because it
    /// is behind you, or out of range, is explained by the picture. One that is dark INSIDE the cone and
    /// INSIDE the arc is explained by elimination -- its normal turned away, or a pillar is in the way.
    public class SensorView : MonoBehaviour
    {
        public Color coneColor = new(0.3f, 0.8f, 1f, 0.9f);
        LineRenderer _line;
        readonly List<LandmarkMarker> _lit = new();

        /// Eye, one FOV edge, the arc at max range, back to the eye. In the Map root's local space (Ship Frame
        /// mapped by ShipFrame.ToUnity), drawn flat on the deck floor at zSurface + 2 cm.
        public static Vector3[] ConeArcPoints(Pose2D pose, double fovDeg, double maxDist, double zSurface, int arcSegments)
        {
            var pts = new Vector3[arcSegments + 3];       // eye + (arcSegments + 1) arc points + back to the eye
            double half = fovDeg * Math.PI / 360.0, z = zSurface + 0.02;
            pts[0] = ShipFrame.ToUnity(pose.x, pose.y, z);
            for (int i = 0; i <= arcSegments; i++)
            {
                double a = pose.psiRad + half - 2 * half * i / arcSegments;   // +half at i=0, -half at i=arcSegments
                pts[i + 1] = ShipFrame.ToUnity(pose.x + Math.Cos(a) * maxDist, pose.y + Math.Sin(a) * maxDist, z);
            }
            pts[arcSegments + 2] = pts[0];
            return pts;
        }

        /// `why` already carries the occlusion verdict, so the eye position is not a parameter here --
        /// whoever built `why` had it, and passing it twice invites the two copies to disagree.
        public void Show(Pose2D pose, double zSurface, IEnumerable<(string id, Miss miss)> why, Func<string, LandmarkMarker> markerOf, double fovDeg, double maxDist)
        {
            foreach (var m in _lit) if (m) m.SetSeen(false);
            _lit.Clear();
            foreach (var (id, miss) in why)
            {
                var mk = markerOf(id); if (!mk) continue;
                if (miss == Miss.None) { mk.SetSeen(true); _lit.Add(mk); }
            }
            EnsureLine();
            var pts = ConeArcPoints(pose, fovDeg, maxDist, zSurface, 24);
            _line.positionCount = pts.Length; _line.SetPositions(pts);
            _line.enabled = true;
        }

        public void Hide()
        {
            foreach (var m in _lit) if (m) m.SetSeen(false);
            _lit.Clear();
            if (_line) _line.enabled = false;
        }

        void EnsureLine()
        {
            if (_line) return;
            // A LineRenderer added straight to this GameObject would collide with NormalGizmo, which
            // MapRuntime.InitForTest attaches to this same Map root: Unity allows only one Renderer per
            // GameObject, so whichever of the two called AddComponent<LineRenderer>() second would get null
            // back and the very next line would throw (or, worse, silently never draw). A child sidesteps
            // the collision instead of racing it.
            var host = new GameObject("SensorCone");
            host.transform.SetParent(transform, false);   // false: local transform stays identity, so host's local space IS the Map root's
            _line = host.AddComponent<LineRenderer>();
            // useWorldSpace = false makes the LineRenderer read SetPositions' points in `host`'s own local
            // space. ConeArcPoints hands it Ship Frame coordinates (via ShipFrame.ToUnity) as if that local
            // space WERE the Map root's directly -- true only because `host` sits at the identity relative to
            // this transform (which is itself the Map root's, since SensorView is a component on it). Moving
            // `host`, or reparenting it under anything with its own offset, shifts or rotates the whole cone
            // away from the pose it was drawn for.
            _line.useWorldSpace = false;
            _line.widthMultiplier = 0.12f; _line.numCornerVertices = 0;
            _line.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = coneColor };
        }
    }
}
