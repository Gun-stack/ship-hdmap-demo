using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// Play-mode placement: left click on ship structure spawns a marker facing the surface normal; right click on a marker deletes it.
    public class LandmarkPlacer : MonoBehaviour
    {
        public Camera cam; public Transform landmarksRoot; public float sizeM = 0.3f;
        public int nextCode = 1; public bool enabledForInput = true;
        public List<LandmarkMarker> All = new();
        public List<Deck> decks = new();   // for deckId lookup by height
        public event Action<LandmarkMarker> Created; public event Action<string> Deleted;

        void Update()
        {
            if (!enabledForInput || cam == null) return;
            if (Input.GetMouseButtonDown(0)) TryPlace();
            if (Input.GetMouseButtonDown(1)) TryDelete();
        }

        public LandmarkMarker PlaceAt(RaycastHit hit)
        {
            string id = $"LM-{All.Count + 1:0000}";
            int code = nextCode++ % AprilTag36h11.Count;
            var lm = LandmarkMarker.Spawn(landmarksRoot, id, code, hit.point, hit.normal, sizeM, DeckIdForHeight(hit.point.y), hit.collider.name);
            All.Add(lm); Created?.Invoke(lm); return lm;
        }

        void TryPlace()
        {
            int mask = LayerMask.GetMask("ShipStructure"); if (mask == 0) mask = ~LayerMask.GetMask("Landmark");
            if (Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit, 500f, mask)) PlaceAt(hit);
        }

        void TryDelete()
        {
            if (!Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit, 500f)) return;
            var lm = hit.collider.GetComponent<LandmarkMarker>(); if (lm == null) return;
            All.Remove(lm); Deleted?.Invoke(lm.id); Destroy(lm.gameObject);
        }

        string DeckIdForHeight(float unityY)
        {
            string best = "D1"; double bestD = double.MaxValue;
            foreach (var d in decks) { double dd = Math.Abs(unityY - d.z_surface); if (unityY + 0.5 >= d.z_surface && dd < bestD) { bestD = dd; best = d.id; } }
            return best;
        }
    }
}
