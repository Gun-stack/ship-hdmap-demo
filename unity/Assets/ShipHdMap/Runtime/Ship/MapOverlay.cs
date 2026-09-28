using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShipHdMap
{
    /// Lane centerlines (lines), parking-slot outlines (lines), parking-slot fills (quads, coloured by status), and parked cars (`PARKED-{slot}`, CarModel), grouped per deck so SetDeck can hide other decks.
    public static class MapOverlay
    {
        const float Lift = 0.05f, FillLift = 0.03f, LaneWidth = 0.15f, SlotWidth = 0.1f, RouteWidth = 0.12f, StrapWidth = 0.035f;
        static readonly Color LaneColor = Palette.C(Palette.Lane), SlotColor = Palette.C(Palette.SlotOutline), RouteColor = Palette.C(Palette.Route);
        public const string RouteSuffix = "~route";
        static readonly Dictionary<Color, Material> LineMats = new();           // shared across Loads; nothing destroys these
        static readonly Dictionary<string, Material> FillMats = new();          // key: status + (selected ? "!" : "")

        public static GameObject Build(VehicleMap map, Transform parent)
        {
            var root = new GameObject("Overlay"); root.transform.SetParent(parent, false);
            foreach (var lane in map.lanes ?? new List<Lane>()) Line(root, lane.deck_id, lane.id, lane.centerline, LaneColor, LaneWidth);
            foreach (var route in map.routes ?? new List<Route>())
            {
                int k = 0;
                foreach (var (deckId, pts) in RoutePieces(route.path, map.decks ?? new List<Deck>())) Line(root, deckId, $"{route.id}{RouteSuffix}{k++}", pts, RouteColor, RouteWidth);
            }
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

        /// A route cut into the pieces each deck filter should show: runs on one deck go to that deck, a ramp between two
        /// decks goes to the upper one (it hangs from it). Consecutive segments of the same deck stay one polyline.
        public static List<(string deck, double[][] pts)> RoutePieces(double[][] path, List<Deck> decks)
        {
            var pieces = new List<(string, double[][])>();
            string DeckAt(double z) => decks.OrderBy(d => System.Math.Abs(d.z_surface - z)).FirstOrDefault()?.id;
            string cur = null; var run = new List<double[]>();
            for (int i = 0; i + 1 < path.Length; i++)
            {
                var a = path[i]; var b = path[i + 1];
                string deck = DeckAt(System.Math.Max(a[2], b[2]));
                if (deck != cur && run.Count > 0) { pieces.Add((cur, run.ToArray())); run = new List<double[]>(); }
                if (run.Count == 0) run.Add(a);
                run.Add(b); cur = deck;
            }
            if (run.Count > 1) pieces.Add((cur, run.ToArray()));
            return pieces;
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
            if (!LineMats.TryGetValue(c, out var m) || !m) { m = Mats.Unlit(c, "line"); LineMats[c] = m; }
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
            // Same names as the web plan view's slot colours (Palette.cs / palette.ts); grey = never got there, nothing to adjust
            var c = status switch {
                "filled" => Palette.C(Palette.SlotFilled, 0.45f),
                "needs_adjust" => Palette.C(Palette.SlotNeedsAdjust, 0.45f),
                "unreachable" => Palette.C(Palette.SlotUnreachable, 0.45f),
                _ => Palette.C(Palette.SlotEmpty, 0.35f) };
            if (selected) c.a = 0.75f;
            m = Mats.Unlit(c, "slotfill-" + key);   // alpha < 1: the unlit, alpha-blended template
            FillMats[key] = m; return m;
        }

        /// Re-colours a slot fill after a parking judgement / unload. Selection highlight is re-applied by the next Highlight call.
        public static void SetStatus(GameObject overlay, string id, string status)
        {
            var slot = FindInDecks(overlay, id); var fill = slot ? slot.GetComponentInChildren<SlotFill>(true) : null;
            if (fill == null) return;
            fill.status = status ?? "empty";
            fill.GetComponent<MeshRenderer>().sharedMaterial = FillMat(fill.status, false);
        }

        /// Static car at the parked pose, under the slot's deck group so SetDeck hides it with the deck. Replaces any previous box.
        public static GameObject SpawnParked(GameObject overlay, ParkingSlot slot, Pose2D pose, double z, IReadOnlyDictionary<string, double[]> lashing = null)
        {
            RemoveParked(overlay, slot.id);
            var deck = overlay.transform.Find(slot.deck_id ?? "none"); if (deck == null) return null;
            // no collider: never blocks placement raycasts or the sensor linecast
            var g = new GameObject("PARKED-" + slot.id); g.transform.SetParent(deck, false);
            g.transform.localPosition = ShipFrame.ToUnity(pose.x, pose.y, z);   // the car mesh's origin is its footprint on the deck
            g.transform.localRotation = Quaternion.Euler(0, ShipFrame.UnityYawDeg(pose.psiRad * 180 / System.Math.PI), 0);
            Renderer own = CarModel.Dress(g, Palette.C(Palette.ParkedCar));
            Straps(g, slot, lashing);
            bool shown = own.enabled;
            foreach (var r in deck.GetComponentsInChildren<Renderer>(true)) if (!r.transform.IsChildOf(g.transform)) { shown = r.enabled; break; }   // inherit the deck filter (SetDeck toggles Renderer.enabled per deck group)
            foreach (var r in g.GetComponentsInChildren<Renderer>(true)) r.enabled = shown;
            return g;
        }

        static Material _strapMat;

        /// Four lashing straps from the car's lower corners down to the slot's own lashing sockets -- the sockets slot
        /// generation mapped to each corner. A slot with fewer than four mapped sockets gets fewer straps: the picture
        /// shows what the map actually holds, which is the point of drawing them.
        static void Straps(GameObject car, ParkingSlot slot, IReadOnlyDictionary<string, double[]> lashing)
        {
            if (lashing == null || slot.lashing_points == null) return;
            if (!_strapMat) _strapMat = Mats.Unlit(Palette.C(Palette.LashingStrap), "strap");
            var root = car.transform.parent;   // the deck group: Ship Frame, like the socket positions
            foreach (var id in slot.lashing_points)
            {
                if (!lashing.TryGetValue(id, out var p)) continue;
                var socket = car.transform.InverseTransformPoint(root.TransformPoint(ShipFrame.ToUnity(p[0], p[1], p[2] + 0.01)));
                // the nearest body corner, low on the chassis
                var corner = new Vector3(Mathf.Sign(socket.x) * (CarModel.LengthM / 2 - 0.5f), 0.35f, Mathf.Sign(socket.z) * (CarModel.WidthM / 2 - 0.1f));
                var s = new GameObject("Strap-" + id); s.transform.SetParent(car.transform, false);
                var lr = s.AddComponent<LineRenderer>();
                lr.useWorldSpace = false; lr.positionCount = 2; lr.SetPosition(0, corner); lr.SetPosition(1, socket);
                lr.startWidth = lr.endWidth = StrapWidth; lr.sharedMaterial = _strapMat;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            }
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
