using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// What a left click does. Sticky: placing a row of markers (12 m apart, 18 of them) is the real job,
    /// so the tool does not snap back to Select after one placement -- the toolbar says which mode is on instead.
    public enum PlacerTool { Select, Place, Probe }

    public enum ClickAct { None, SelectMarker, DragMarker, Place, Probe, Clear }

    /// Play-mode mouse. What a left click does depends on `tool` (M5e): Select picks and drags, Place spawns
    /// on ship structure facing the surface normal, Probe drops the virtual viewpoint. Deletion happens only
    /// from the web form (DB first, then Delete(id)), so there is no right-click delete.
    public class LandmarkPlacer : MonoBehaviour
    {
        public Camera cam; public Transform landmarksRoot; public float sizeM = 0.3f;
        public int nextCode = 1; public bool enabledForInput = true;
        public int nextId = 1;
        public string NextId() => $"LM-{nextId++:0000}";
        public List<LandmarkMarker> All = new();
        public List<Deck> decks = new();   // for deckId lookup by height
        public PlacerTool tool = PlacerTool.Select;
        public event Action<LandmarkMarker> Created; public event Action<LandmarkMarker> Selected; public event Action<LandmarkMarker> Moved;
        public event Action<RaycastHit> ProbeAt; public event Action Cleared;
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

        /// The click rule, with no Input or Physics in it so an EditMode test can pin it down.
        /// A marker under the cursor always wins -- it is the only handle on a marker in any tool.
        public static ClickAct Decide(PlacerTool tool, bool hitMarker, bool hitStructure)
        {
            if (hitMarker) return tool == PlacerTool.Select ? ClickAct.DragMarker : ClickAct.SelectMarker;
            if (!hitStructure) return tool == PlacerTool.Select ? ClickAct.Clear : ClickAct.None;
            switch (tool)
            {
                case PlacerTool.Place: return ClickAct.Place;
                case PlacerTool.Probe: return ClickAct.Probe;
                default: return ClickAct.Clear;
            }
        }

        void OnLeftDown()
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            int lmMask = LayerMask.GetMask("Landmark");
            LandmarkMarker lm = null; float lmDist = float.MaxValue;
            if (lmMask != 0 && Physics.Raycast(ray, out var mh, 500f, lmMask)) { mh.collider.TryGetComponent(out lm); lmDist = mh.distance; }
            bool hitStructure = Physics.Raycast(ray, out var hit, 500f, StructureMask());
            // With the deck filter on "all", every deck's markers are live, so a marker two decks down can sit
            // behind the hull face the user aimed at. Only count it as "hit" when it is actually the nearer thing --
            // otherwise a marker anywhere along the ray steals a click meant to place or probe the surface.
            bool hitMarker = lm != null && (!hitStructure || lmDist <= hit.distance);
            switch (Decide(tool, hitMarker, hitStructure))
            {
                case ClickAct.DragMarker: _drag = lm; _dragStart = lm.transform.position; _moved = false; Selected?.Invoke(lm); break;
                case ClickAct.SelectMarker: Selected?.Invoke(lm); break;
                case ClickAct.Place: PlaceAt(hit); break;
                case ClickAct.Probe: ProbeAt?.Invoke(hit); break;
                case ClickAct.Clear: Cleared?.Invoke(); break;
            }
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
