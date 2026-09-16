using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// Play-mode mouse: left click on a marker selects it, left click on ship structure spawns a marker facing the surface normal.
    /// Deletion happens only from the web form (DB first, then Delete(id)), so there is no right-click delete.
    public class LandmarkPlacer : MonoBehaviour
    {
        public Camera cam; public Transform landmarksRoot; public float sizeM = 0.3f;
        public int nextCode = 1; public bool enabledForInput = true;
        public int nextId = 1;
        public string NextId() => $"LM-{nextId++:0000}";
        public List<LandmarkMarker> All = new();
        public List<Deck> decks = new();   // for deckId lookup by height
        public event Action<LandmarkMarker> Created; public event Action<LandmarkMarker> Selected; public event Action<LandmarkMarker> Moved;
        LandmarkMarker _drag; Vector3 _dragStart; bool _moved;

        void Update()
        {
            if (!enabledForInput || cam == null) { EndDrag(); return; } // flush any in-progress drag so a moved marker still gets reported before input is cut
            if (Input.GetMouseButtonDown(0)) OnLeftDown();
            else if (_drag && Input.GetMouseButton(0)) DragTo();
            if (_drag && Input.GetMouseButtonUp(0)) EndDrag();
        }

        public LandmarkMarker PlaceAt(RaycastHit hit)
        {
            string id = NextId();
            int code = nextCode++ % AprilTag36h11.Count;
            // Spawn takes root-local input; the raycast hit is world.
            var lm = LandmarkMarker.Spawn(landmarksRoot, id, code, landmarksRoot.InverseTransformPoint(hit.point), landmarksRoot.InverseTransformDirection(hit.normal),
                sizeM, DeckIdForHeight(LocalY(hit.point)), hit.collider.name);
            All.Add(lm); Created?.Invoke(lm); return lm;
        }

        public static int StructureMask() { int mask = LayerMask.GetMask("ShipStructure"); return mask == 0 ? ~LayerMask.GetMask("Landmark") : mask; }

        void OnLeftDown()
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            int lmMask = LayerMask.GetMask("Landmark");
            if (lmMask != 0 && Physics.Raycast(ray, out var mh, 500f, lmMask) && mh.collider.TryGetComponent<LandmarkMarker>(out var lm)) { _drag = lm; _dragStart = lm.transform.position; _moved = false; Selected?.Invoke(lm); return; }
            if (Physics.Raycast(ray, out var hit, 500f, StructureMask())) PlaceAt(hit);
        }

        void DragTo()
        {
            if (!Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit, 500f, StructureMask())) return; // marker colliders are on Landmark, so they never block the drag ray
            _drag.MoveTo(hit.point, hit.normal, DeckIdForHeight(LocalY(hit.point)), hit.collider.name);
            if ((_drag.transform.position - _dragStart).sqrMagnitude > 1e-4f) _moved = true;
        }

        void EndDrag() { if (_moved) Moved?.Invoke(_drag); _drag = null; _moved = false; }

        /// Deck lookup must use the Map-root-local height: with a 2 deg trim the bow floor is 4 m higher in world space.
        float LocalY(Vector3 world) => landmarksRoot ? landmarksRoot.InverseTransformPoint(world).y : world.y;

        public string DeckIdForHeight(float unityY)
        {
            string best = "D1"; double bestD = double.MaxValue;
            foreach (var d in decks) { double dd = Math.Abs(unityY - d.z_surface); if (unityY + 0.5 >= d.z_surface && dd < bestD) { bestD = dd; best = d.id; } }
            return best;
        }
    }
}
