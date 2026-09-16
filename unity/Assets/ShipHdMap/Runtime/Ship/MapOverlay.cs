using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// Lane centerlines and parking-slot outlines as LineRenderers, grouped per deck so SetDeck can hide other decks.
    public static class MapOverlay
    {
        const float Lift = 0.05f, LaneWidth = 0.15f, SlotWidth = 0.1f;
        static readonly Color LaneColor = new Color(1f, 0.85f, 0.2f), SlotColor = new Color(0.2f, 0.9f, 0.4f);

        public static GameObject Build(VehicleMap map, Transform parent)
        {
            var root = new GameObject("Overlay"); root.transform.SetParent(parent, false);
            var mats = new Dictionary<Color, Material>();
            foreach (var lane in map.lanes ?? new List<Lane>()) Line(root, lane.deck_id, lane.id, lane.centerline, LaneColor, LaneWidth, mats);
            foreach (var slot in map.parking_slots ?? new List<ParkingSlot>()) Line(root, slot.deck_id, slot.id, slot.polygon, SlotColor, SlotWidth, mats);
            return root;
        }

        public static void SetDeck(GameObject overlay, string deckOrAll)
        {
            foreach (Transform deck in overlay.transform)
                foreach (var lr in deck.GetComponentsInChildren<LineRenderer>(true)) lr.enabled = deckOrAll == "all" || deck.name == deckOrAll;
        }

        static void Line(GameObject root, string deckId, string id, double[][] pts, Color c, float width, Dictionary<Color, Material> mats)
        {
            if (pts == null || pts.Length < 2) return;
            var deck = root.transform.Find(deckId ?? "none");
            if (deck == null) { deck = new GameObject(deckId ?? "none").transform; deck.SetParent(root.transform, false); }
            var g = new GameObject(id); g.transform.SetParent(deck, false);
            var lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.loop = false; lr.startWidth = lr.endWidth = width; lr.positionCount = pts.Length; // polygons already repeat their first point
            for (int i = 0; i < pts.Length; i++) lr.SetPosition(i, ShipFrame.ToUnity(pts[i][0], pts[i][1], pts[i][2] + Lift));
            if (!mats.TryGetValue(c, out var m)) { m = new Material(Shader.Find("Unlit/Color")) { color = c }; mats[c] = m; }
            lr.sharedMaterial = m; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        }
    }
}
