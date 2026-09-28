using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShipHdMap
{
    public static class ShipMeshBuilder
    {
        const float FloorThick = 0.2f, WallThick = 0.2f, LashRadius = 0.06f;

        /// Build the hull from the map instead of the generator's parameters: L and B come from each deck's own outline,
        /// so a different ship model in the DB gives a different hull without touching ShipParams (spec M5b §3).
        public static GameObject Build(VehicleMap map, Transform parent = null)
        {
            var seed = new SeedData
            {
                decks = map.decks ?? new List<Deck>(),
                facilities = map.facilities ?? new List<Facility>(),
                lashing_points = map.lashing_points ?? new List<LashingPoint>(),
                ramps = map.ramps ?? new List<Ramp>(),
                lanes = map.lanes ?? new List<Lane>(),
            };
            return Build(seed, parent);
        }

        public static GameObject Build(SeedData seed, Transform parent = null)
        {
            var ship = new GameObject("Ship"); if (parent) ship.transform.SetParent(parent, false);
            int layer = LayerMask.NameToLayer("ShipStructure"); if (layer < 0) layer = 0;
            var materials = new Dictionary<Color, Material>(); // one shared Material per colour so identical-colour primitives don't each allocate their own
            foreach (var d in seed.decks)
            {
                // A deck with no outline has no hull to draw. Skip it rather than throwing halfway through a Load and
                // leaving a half-built scene -- MapRuntime.ShipSignature already tolerates the same map the same way.
                if (d.outline == null || d.outline.Length == 0) continue;
                var deck = Child(ship, d.id);
                // Both the size AND the position come from the deck's own outline: a deck that starts forward of the AP,
                // or whose centreline is not ship y = 0, is drawn where the map puts it instead of at the origin.
                double x0 = d.outline.Min(pt => pt[0]), x1 = d.outline.Max(pt => pt[0]);
                double y0 = d.outline.Min(pt => pt[1]), y1 = d.outline.Max(pt => pt[1]);
                float z = (float)d.z_surface, L = (float)(x1 - x0), B = (float)(y1 - y0);
                float deckCx = (float)((x0 + x1) / 2), deckCz = -(float)((y0 + y1) / 2);   // Unity z = -ship y, so port (+y) is -z
                // The floor minus the openings internal ramps swing into (M8): one mesh, and the same mesh as its collider, so
                // a click or a sensor sight line goes through an opening exactly where the eye does.
                var holes = seed.ramps.Where(r => r.type == "internal_hoistable" && r.upper_deck == d.id && r.toe != null).Select(Footprint).ToList();
                var floor = Slab(deck, "Floor", x0, y0, x1, y1, z, holes, Palette.C(Palette.DeckFloor), layer, materials);
                Prim(deck, "HullPort", PrimitiveType.Cube, new Vector3(deckCx, z + (float)d.z_clear / 2, deckCz - B / 2), new Vector3(L, (float)d.z_clear, WallThick), Palette.C(Palette.HullWall), layer, materials);
                Prim(deck, "HullStbd", PrimitiveType.Cube, new Vector3(deckCx, z + (float)d.z_clear / 2, deckCz + B / 2), new Vector3(L, (float)d.z_clear, WallThick), Palette.C(Palette.HullWall), layer, materials);
                // Bow bulkhead: closes the deck at the forward end so landmarks placed there sit on structure and occlude like the hull.
                Prim(deck, "Bow", PrimitiveType.Cube, new Vector3((float)x1 - WallThick / 2, z + (float)d.z_clear / 2, deckCz), new Vector3(WallThick, (float)d.z_clear, B), Palette.C(Palette.HullWall), layer, materials);
                var pillars = Child(deck, "Pillars");
                foreach (var f in seed.facilities) if (f.deck_id == d.id && f.kind == "pillar")
                {
                    float cx = (float)(f.footprint[0][0] + f.footprint[2][0]) / 2, cy = (float)(f.footprint[0][1] + f.footprint[2][1]) / 2;
                    float sx = (float)(f.footprint[2][0] - f.footprint[0][0]), sy = (float)(f.footprint[2][1] - f.footprint[0][1]), h = (float)(f.z_max - f.z_min);
                    Prim(pillars, f.id, PrimitiveType.Cube, ShipFrame.ToUnity(cx, cy, f.z_min + h / 2), new Vector3(sx, h, sy), Palette.C(Palette.Pillar), layer, materials);
                }
                // Every socket on the deck in ONE mesh (a flat hexagon each): five decks of ~4,700 sockets as GameObjects
                // was tens of thousands of objects. Visual only, no collider; none drawn inside an opening.
                var sockets = seed.lashing_points.Where(lp => lp.deck_id == d.id && !holes.Any(h => Inside(h, lp.position[0], lp.position[1]))).ToList();
                if (sockets.Count > 0) Sockets(deck, sockets, Palette.C(Palette.LashingSocket), materials);
            }
            foreach (var r in seed.ramps)
            {
                bool inner = r.type == "internal_hoistable";
                // The stern ramp keeps the plain name every caller (SetRampAngle, the tests) finds it by; internal ramps are
                // named for their id so there is exactly one "Ramp" whatever order the map lists ramps in.
                var ramp = Child(ship, inner ? InnerRampName(r.id) : "Ramp");
                ramp.transform.localPosition = ShipFrame.ToUnity((r.hinge[0][0] + r.hinge[1][0]) / 2, (r.hinge[0][1] + r.hinge[1][1]) / 2, r.hinge[0][2]);
                float len = (float)r.length_m, w = (float)r.width_m;
                // Stern: the plate runs astern (-x) from the hinge. Internal: toward its toe, which may be fore or aft.
                float dir = inner && r.toe != null && r.toe[0][0] > r.hinge[0][0] ? 1f : -1f;
                Prim(ramp, "Plate", PrimitiveType.Cube, new Vector3(dir * len / 2, -FloorThick / 2, 0), new Vector3(len, FloorThick, w), Palette.C(Palette.RampPlate), layer, materials);   // local to the hinge
                if (inner)
                {
                    var hoist = ramp.AddComponent<RampHoist>(); hoist.lowerZ = (float)Math.Min(r.hinge[0][2], r.toe[0][2]);
                    hoist.deployedDeg = -dir * (float)Math.Asin(Math.Min(1, (r.hinge[0][2] - r.toe[0][2]) / r.length_m)) * Mathf.Rad2Deg;   // toe end down onto the lower deck
                    hoist.Set(true, animate: false);
                }
            }
            HullBuilder.Build(ship, seed.decks, seed.ramps.FirstOrDefault(r => (r.type ?? "stern_quarter") == "stern_quarter"), materials);
            HullBuilder.SetCutaway(ship, true);
            Physics.SyncTransforms(); // project has autoSyncTransforms off; Collider.bounds needs a manual sync after transform edits
            return ship;
        }

        /// Positive angle lifts the free (stern, -x) end. Hinge line is along Unity Z, so rotate about Z.
        /// angleDeg is measured from the API against the horizon (it comes from two heights: quay surface and
        /// hinge), but this rotation is ship-LOCAL. A positive trim (stern deeper, bow up) is itself a positive
        /// rotation about Unity Z, which pushes the free end (at ship -x) DOWN by trim -- so trimDeg must be added
        /// on top of angleDeg for the ramp to still meet the horizon-relative angle the API gave.
        public static void SetRampAngle(GameObject ship, double angleDeg, double trimDeg)
        {
            var ramp = ship.transform.Find("Ramp"); if (!ramp) return;
            ramp.localRotation = Quaternion.Euler(0, 0, (float)-(angleDeg + trimDeg));
            // Flushes ALL pending transforms scene-wide (autoSyncTransforms is off in this project). Call this on
            // pose changes only (e.g. an operator moving the ramp), not every frame.
            Physics.SyncTransforms();
        }

        public static string InnerRampName(string id) => "Ramp-" + id;

        /// Plan-view rectangle {x0, y0, x1, y1} between an internal ramp's hinge and toe.
        static double[] Footprint(Ramp r) => new[] { Math.Min(r.hinge[0][0], r.toe[0][0]), Math.Min(r.hinge[0][1], r.hinge[1][1]), Math.Max(r.hinge[0][0], r.toe[0][0]), Math.Max(r.hinge[0][1], r.hinge[1][1]) };
        static bool Inside(double[] h, double x, double y) => x > h[0] && x < h[2] && y > h[1] && y < h[3];

        /// A deck slab [x0,x1] x [y0,y1] at top height z with rectangular holes, as boxes: cut into x-strips at every hole
        /// edge, then each strip into y-intervals around the holes that span it.
        public static List<(double x0, double y0, double x1, double y1)> SlabPieces(double x0, double y0, double x1, double y1, IList<double[]> holes)
        {
            var xs = new SortedSet<double> { x0, x1 };
            foreach (var h in holes) { if (h[0] > x0 && h[0] < x1) xs.Add(h[0]); if (h[2] > x0 && h[2] < x1) xs.Add(h[2]); }
            var cuts = xs.ToList(); var pieces = new List<(double, double, double, double)>();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double a = cuts[i], b = cuts[i + 1], mid = (a + b) / 2;
                var gaps = holes.Where(h => mid > h[0] && mid < h[2]).Select(h => (h[1], h[3])).OrderBy(g => g.Item1).ToList();
                double y = y0;
                foreach (var (g0, g1) in gaps) { if (g0 > y) pieces.Add((a, y, b, g0)); y = Math.Max(y, g1); }
                if (y < y1) pieces.Add((a, y, b, y1));
            }
            return pieces;
        }

        static GameObject Slab(GameObject parent, string name, double x0, double y0, double x1, double y1, float z, IList<double[]> holes, Color c, int layer, Dictionary<Color, Material> materials)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            foreach (var (a, b, e, f) in SlabPieces(x0, y0, x1, y1, holes)) AddBox(v, n, t, ShipFrame.ToUnity((a + e) / 2, (b + f) / 2, z - FloorThick / 2), new Vector3((float)(e - a), FloorThick, (float)(f - b)));
            var mesh = new Mesh { name = name + "~gen", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            var g = Child(parent, name); g.layer = layer;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (!materials.TryGetValue(c, out var mat)) { mat = Mats.Lit(c, 0.3f); materials[c] = mat; }
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            g.AddComponent<MeshCollider>().sharedMesh = mesh;
            return g;
        }

        static void AddBox(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 c, Vector3 size)
        {
            var h = size / 2;
            (Vector3 nrm, Vector3 u, Vector3 w)[] faces = { (Vector3.up, Vector3.right, Vector3.forward), (Vector3.down, Vector3.forward, Vector3.right),
                (Vector3.right, Vector3.forward, Vector3.up), (Vector3.left, Vector3.up, Vector3.forward), (Vector3.forward, Vector3.up, Vector3.right), (Vector3.back, Vector3.right, Vector3.up) };
            foreach (var (nrm, u, w) in faces)
            {
                var o = c + Vector3.Scale(nrm, h); var du = Vector3.Scale(u, h); var dw = Vector3.Scale(w, h);
                int b = v.Count;
                v.Add(o - du - dw); v.Add(o - du + dw); v.Add(o + du + dw); v.Add(o + du - dw);
                for (int k = 0; k < 4; k++) n.Add(nrm);
                t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
        }

        static void Sockets(GameObject deck, List<LashingPoint> sockets, Color c, Dictionary<Color, Material> materials)
        {
            var v = new List<Vector3>(sockets.Count * 7); var t = new List<int>(sockets.Count * 18);
            foreach (var lp in sockets)
            {
                var p = ShipFrame.ToUnity(lp.position[0], lp.position[1], lp.position[2] + 0.006);
                int b = v.Count; v.Add(p);
                for (int k = 0; k < 6; k++) { float a = k * Mathf.PI / 3; v.Add(p + new Vector3(Mathf.Cos(a) * LashRadius, 0, Mathf.Sin(a) * LashRadius)); }
                for (int k = 0; k < 6; k++) t.AddRange(new[] { b, b + 1 + (k + 1) % 6, b + 1 + k });
            }
            var mesh = new Mesh { name = "Lashing~gen", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var g = Child(deck, "Lashing");
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (!materials.TryGetValue(c, out var mat)) { mat = Mats.Lit(c, 0.5f, 0.6f); materials[c] = mat; }
            var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// Raise (stowed: flush with the upper deck, closing its opening) or lower (deployed: the toe on the lower deck) one
        /// internal ramp. animate eases it over a few seconds in play; tests and loads set it at once.
        public static void SetInnerRamp(GameObject ship, string id, bool deployed, bool animate)
        {
            var t = ship ? ship.transform.Find(InnerRampName(id)) : null;
            var hoist = t ? t.GetComponent<RampHoist>() : null;
            if (hoist) hoist.Set(deployed, animate);
        }

        /// One deck in view: decks above it are hidden (with five stacked decks a faded roof over the one you are looking at
        /// is still a roof), decks below are faded, it is solid. "all": every deck solid. The outer shell hides with the
        /// decks: in single-deck view its roof and upper topsides would stand between the camera and the deck.
        public static void SetDeckVisibility(GameObject ship, string deckIdOrAll)
        {
            double? selZ = null;
            foreach (Transform deck in ship.transform)
            {
                if (deck.name == deckIdOrAll) { var f = deck.Find("Floor"); if (f) selZ = f.GetComponent<Renderer>().bounds.max.y; }
            }
            foreach (Transform deck in ship.transform)
            {
                if (deck.name == HullBuilder.Root) { HullBuilder.Show(ship, deckIdOrAll == "all"); continue; }
                if (deck.name.StartsWith("Ramp"))
                {
                    // an internal ramp between two decks both above the one in view floats over it: hide it with them
                    var hoist = deck.GetComponent<RampHoist>();
                    bool overhead = hoist && selZ.HasValue && hoist.lowerZ > selZ.Value + 0.1;
                    foreach (var r in deck.GetComponentsInChildren<Renderer>(true)) r.enabled = !overhead;
                    continue;
                }
                bool visible = deckIdOrAll == "all" || deck.name == deckIdOrAll;
                var fl = deck.Find("Floor");
                bool above = !visible && selZ.HasValue && fl && fl.GetComponent<Renderer>().bounds.max.y > selZ.Value + 0.1;
                foreach (var r in deck.GetComponentsInChildren<Renderer>(true)) { r.enabled = !above; SetAlpha(r, visible ? 1f : 0.15f); }
            }
        }

        static GameObject Child(GameObject parent, string name) { var g = new GameObject(name); g.transform.SetParent(parent.transform, false); return g; }

        static GameObject Prim(GameObject parent, string name, PrimitiveType t, Vector3 pos, Vector3 scale, Color c, int layer, Dictionary<Color, Material> materials)
        {
            var g = GameObject.CreatePrimitive(t); g.name = name; g.layer = layer; g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = pos; g.transform.localScale = scale;
            if (!materials.TryGetValue(c, out var mat)) { mat = Mats.Lit(c, 0.3f); materials[c] = mat; }
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        static void SetAlpha(Renderer r, float a)
        {
            // Give this renderer its own material instance, decoupled from the shared cached material above --
            // otherwise fading one primitive would fade every other primitive of the same colour. Cloning by hand
            // (rather than reading renderer.material) avoids Unity's edit-mode "instantiating material" warning/error.
            // Clone once per renderer (marked by a "~inst" name suffix) and reuse it on later calls, so repeated
            // deck-switch clicks don't leak a fresh Material per renderer every time.
            var m = r.sharedMaterial;
            if (!m.name.EndsWith("~inst")) { m = new Material(m) { name = m.name + "~inst" }; r.sharedMaterial = m; }
            Mats.SetFade(m, a);
        }
    }
}
