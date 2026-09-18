using System;
using UnityEngine;

namespace ShipHdMap
{
    /// A circular handle around the selected marker. Dragging it turns the marker's normal in the deck plane;
    /// letting go raises Rotated, which MapRuntime feeds into the SAME onFeatureMoved path a drag-move uses --
    /// the web already persists `normal` from that event, so no new message exists for this.
    ///
    /// Why it matters: M5c and M5d both found the normal decides coverage far more than the position does,
    /// and until now the only way to change one was to hand-edit props JSON.
    public class NormalGizmo : MonoBehaviour
    {
        public Camera cam; public Transform root; public OrbitCamera orbit; public float radiusM = 1.6f;
        public event Action<LandmarkMarker> Rotated;
        LandmarkMarker _marker; LineRenderer _ring; bool _dragging;

        public LandmarkMarker Target => _marker;

        /// Angle from `centerUnity` to `pointUnity` in Ship Frame, wrapped to (-pi, pi]. Height is dropped:
        /// the handle is a circle on the deck plane.
        public static double PhiAt(Vector3 centerUnity, Vector3 pointUnity)
        {
            var (cx, cy, _) = ShipFrame.ToShip(centerUnity);
            var (px, py, _) = ShipFrame.ToShip(pointUnity);
            return ShipFrame.WrapRad(Math.Atan2(py - cy, px - cx));
        }

        public void Attach(LandmarkMarker m) { _marker = m; _dragging = false; DrawRing(); }
        public void Detach() { if (_dragging && orbit) orbit.enabled = true; _marker = null; _dragging = false; if (_ring) _ring.enabled = false; }

        void Update()
        {
            if (_marker == null || cam == null) { if (_dragging) EndDrag(); return; }
            // Right button, so the gizmo never competes with LandmarkPlacer's left-click tools. That means it
            // shares a button with the orbit camera, so a drag that starts on the ring switches the camera off
            // for its duration -- otherwise turning a normal also spins the view. The ring is a thin annulus,
            // so every other right-drag still orbits.
            if (Input.GetMouseButtonDown(1) && OnRing()) { _dragging = true; if (orbit) orbit.enabled = false; }
            else if (_dragging && Input.GetMouseButton(1)) DragTo();
            if (_dragging && Input.GetMouseButtonUp(1)) EndDrag();
        }

        bool OnRing()
        {
            if (!PlanePoint(out var p)) return false;
            return Mathf.Abs(Vector3.Distance(p, _marker.transform.position) - radiusM) < radiusM * 0.5f;
        }

        void DragTo()
        {
            if (!PlanePoint(out var p)) return;
            // Both points go through `root` first: PhiAt reads Ship Frame, and the marker's world transform is
            // the Ship Frame rotated by trim and heel. Reading world coordinates as if they were Ship Frame
            // pollutes x with the marker's height (11.8 m at 2 deg trim is 0.4 m) -- degrees of error on a 1.6 m ring.
            double phi = PhiAt(ToRoot(_marker.transform.position), ToRoot(p));
            var nLocal = ShipFrame.ToUnity(Math.Cos(phi), Math.Sin(phi), 0);
            // MoveTo takes WORLD; keep the mounting point where it is and turn only the facing
            var n = root ? root.TransformDirection(nLocal) : nLocal;
            _marker.MoveTo(_marker.transform.position - _marker.NormalUnity * 0.01f, n, _marker.deckId, _marker.mountedOn);
            DrawRing();
        }

        Vector3 ToRoot(Vector3 world) => root ? root.InverseTransformPoint(world) : world;

        void EndDrag() { bool was = _dragging; _dragging = false; if (was && orbit) orbit.enabled = true; if (was && _marker) Rotated?.Invoke(_marker); }

        /// Mouse ray against the horizontal plane through the marker.
        bool PlanePoint(out Vector3 p)
        {
            p = default;
            var plane = new Plane(Vector3.up, _marker.transform.position);
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!plane.Raycast(ray, out float t)) return false;
            p = ray.GetPoint(t); return true;
        }

        void DrawRing()
        {
            if (_ring == null)
            {
                _ring = gameObject.AddComponent<LineRenderer>();
                _ring.useWorldSpace = true; _ring.widthMultiplier = 0.05f; _ring.loop = true;
                _ring.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.72f, 0f) };
            }
            var c = _marker.transform.position;
            var pts = new Vector3[36];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = i * Mathf.PI * 2f / pts.Length;
                pts[i] = c + new Vector3(Mathf.Cos(a) * radiusM, 0, Mathf.Sin(a) * radiusM);
            }
            _ring.positionCount = pts.Length; _ring.SetPositions(pts);
            _ring.enabled = true;
        }
    }
}
