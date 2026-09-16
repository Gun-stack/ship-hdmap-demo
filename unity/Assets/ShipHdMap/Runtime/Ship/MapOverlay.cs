using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// Lane centerlines (lines), parking-slot outlines (lines), parking-slot fills (quads, coloured by status), and parked-car boxes (`PARKED-{slot}`), grouped per deck so SetDeck can hide other decks.
    public static class MapOverlay
    {
        const float Lift = 0.05f, FillLift = 0.03f, LaneWidth = 0.15f, SlotWidth = 0.1f;
        static readonly Color LaneColor = new Color(1f, 0.85f, 0.2f), SlotColor = new Color(0.2f, 0.9f, 0.4f);
        static readonly Dictionary<Color, Material> LineMats = new();           // shared across Loads; nothing destroys these
        static readonly Dictionary<string, Material> FillMats = new();          // key: status + (selected ? "!" : "")

        public static GameObject Build(VehicleMap map, Transform parent)
        {
            var root = new GameObject("Overlay"); root.transform.SetParent(parent, false);
            foreach (var lane in map.lanes ?? new List<Lane>()) Line(root, lane.deck_id, lane.id, lane.centerline, LaneColor, LaneWidth);
            foreach (var slot in map.parking_slots ?? new List<ParkingSlot>())
            {
                var g = Line(root, slot.deck_id, slot.id, slot.polygon, SlotColor, SlotWidth);
                if (g != null) Fill(g, slot);
            }
            return root;
        }

        public static void SetDeck(GameObject overlay, string deckOrAll)
        {
            foreach (Transform deck in overlay.transform)
            {
                bool on = deckOrAll == "all" || deck.name == deckOrAll;
                foreach (var r in deck.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            }
        }

        /// Selected slot gets the bright variant of its status colour; null or "" clears all.
        public static void Highlight(GameObject overlay, string id)
        {
            foreach (var mf in overlay.GetComponentsInChildren<SlotFill>(true))
                mf.GetComponent<MeshRenderer>().sharedMaterial = FillMat(mf.status, !string.IsNullOrEmpty(id) && mf.transform.parent.name == id);
        }

        static GameObject Line(GameObject root, string deckId, string id, double[][] pts, Color c, float width)
        {
            if (pts == null || pts.Length < 2) return null;
            var deck = root.transform.Find(deckId ?? "none");
            if (deck == null) { deck = new GameObject(deckId ?? "none").transform; deck.SetParent(root.transform, false); }
            var g = new GameObject(id); g.transform.SetParent(deck, false);
            var lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = false; lr.loop = false; lr.startWidth = lr.endWidth = width; lr.positionCount = pts.Length;
            for (int i = 0; i < pts.Length; i++) lr.SetPosition(i, ShipFrame.ToUnity(pts[i][0], pts[i][1], pts[i][2] + Lift));
            if (!LineMats.TryGetValue(c, out var m) || !m) { m = new Material(Shader.Find("Unlit/Color")) { color = c }; LineMats[c] = m; }
            lr.sharedMaterial = m; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            return g;
        }

        /// Translucent quad over the slot polygon (first four ring points), coloured by status.
        static void Fill(GameObject slotGo, ParkingSlot slot)
        {
            if (slot.polygon == null || slot.polygon.Length < 4) return;
            var f = new GameObject("Fill"); f.transform.SetParent(slotGo.transform, false);
            var mesh = new Mesh { name = slot.id + "~fill" };
            var v = new Vector3[4]; for (int i = 0; i < 4; i++) v[i] = ShipFrame.ToUnity(slot.polygon[i][0], slot.polygon[i][1], slot.polygon[i][2] + FillLift);
            mesh.vertices = v; mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }; // both windings so it reads from above and below
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            f.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = f.AddComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            var tag = f.AddComponent<SlotFill>(); tag.status = slot.status ?? "empty";
            mr.sharedMaterial = FillMat(tag.status, false);
        }

        static Material FillMat(string status, bool selected)
        {
            string key = status + (selected ? "!" : "");
            if (FillMats.TryGetValue(key, out var m) && m) return m;
            var c = status switch { "filled" => new Color(0.2f, 0.5f, 1f, 0.45f), "needs_adjust" => new Color(1f, 0.6f, 0.1f, 0.45f), _ => new Color(0.2f, 0.9f, 0.4f, 0.35f) };
            if (selected) c.a = 0.75f;
            m = new Material(Shader.Find("Sprites/Default")) { color = c, name = "slotfill-" + key }; // always-included, unlit, alpha-blended
            FillMats[key] = m; return m;
        }

        static Material _parkedMat;

        /// Re-colours a slot fill after a parking judgement / unload. Selection highlight is re-applied by the next Highlight call.
        public static void SetStatus(GameObject overlay, string id, string status)
        {
            var slot = FindInDecks(overlay, id); var fill = slot ? slot.GetComponentInChildren<SlotFill>(true) : null;
            if (fill == null) return;
            fill.status = status ?? "empty";
            fill.GetComponent<MeshRenderer>().sharedMaterial = FillMat(fill.status, false);
        }

        /// Static car box at the parked pose, under the slot's deck group so SetDeck hides it with the deck. Replaces any previous box.
        public static GameObject SpawnParked(GameObject overlay, ParkingSlot slot, Pose2D pose, double z)
        {
            RemoveParked(overlay, slot.id);
            var deck = overlay.transform.Find(slot.deck_id ?? "none"); if (deck == null) return null;
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = "PARKED-" + slot.id; g.transform.SetParent(deck, false);
            Object.DestroyImmediate(g.GetComponent<Collider>());   // never blocks placement raycasts or the sensor linecast
            g.transform.localPosition = ShipFrame.ToUnity(pose.x, pose.y, z + 0.75);
            g.transform.localRotation = Quaternion.Euler(0, ShipFrame.UnityYawDeg(pose.psiRad * 180 / System.Math.PI), 0);
            g.transform.localScale = new Vector3(4.8f, 1.5f, 1.85f);
            if (!_parkedMat) _parkedMat = new Material(Shader.Find("Standard")) { color = new Color(0.82f, 0.84f, 0.9f), name = "parked-car" };
            var own = g.GetComponent<Renderer>(); own.sharedMaterial = _parkedMat;
            foreach (var r in deck.GetComponentsInChildren<Renderer>(true)) if (r != own) { own.enabled = r.enabled; break; }   // inherit the deck filter (SetDeck toggles Renderer.enabled per deck group)
            return g;
        }

        public static void RemoveParked(GameObject overlay, string slotId)
        {
            var t = FindInDecks(overlay, "PARKED-" + slotId); if (!t) return;
            if (Application.isPlaying) Object.Destroy(t.gameObject); else Object.DestroyImmediate(t.gameObject);
        }

        static Transform FindInDecks(GameObject overlay, string name)
        {
            if (!overlay) return null;
            foreach (Transform deck in overlay.transform) { var t = deck.Find(name); if (t) return t; }
            return null;
        }
    }

    /// Marks a slot fill quad and remembers its status for re-colouring on selection.
    public class SlotFill : MonoBehaviour
    {
        public string status = "empty";

        /// Play-mode backstop for a fill destroyed on its own. In EditMode (tests) OnDestroy never runs because
        /// SlotFill has no [ExecuteAlways], so MapRuntime.Load frees fill meshes explicitly before destroying the overlay.
        void OnDestroy()
        {
            var m = GetComponent<MeshFilter>()?.sharedMesh;
            if (!m) return;
            if (Application.isPlaying) Destroy(m); else DestroyImmediate(m);
        }
    }
}
