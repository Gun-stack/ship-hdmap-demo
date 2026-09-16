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
        public event Action<LandmarkMarker> Created; public event Action<LandmarkMarker> Selected;

        void Update()
        {
            if (!enabledForInput || cam == null) return;
            if (Input.GetMouseButtonDown(0)) OnLeftDown();
        }

        public LandmarkMarker PlaceAt(RaycastHit hit)
        {
            string id = NextId();
            int code = nextCode++ % AprilTag36h11.Count;
            var lm = LandmarkMarker.Spawn(landmarksRoot, id, code, hit.point, hit.normal, sizeM, DeckIdForHeight(hit.point.y), hit.collider.name);
            All.Add(lm); Created?.Invoke(lm); return lm;
        }

        public static int StructureMask() { int mask = LayerMask.GetMask("ShipStructure"); return mask == 0 ? ~LayerMask.GetMask("Landmark") : mask; }

        void OnLeftDown()
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            int lmMask = LayerMask.GetMask("Landmark");
            if (lmMask != 0 && Physics.Raycast(ray, out var mh, 500f, lmMask) && mh.collider.TryGetComponent<LandmarkMarker>(out var lm)) { Selected?.Invoke(lm); return; }
            if (Physics.Raycast(ray, out var hit, 500f, StructureMask())) PlaceAt(hit);
        }

        public string DeckIdForHeight(float unityY)
        {
            string best = "D1"; double bestD = double.MaxValue;
            foreach (var d in decks) { double dd = Math.Abs(unityY - d.z_surface); if (unityY + 0.5 >= d.z_surface && dd < bestD) { bestD = dd; best = d.id; } }
            return best;
        }
    }
}
