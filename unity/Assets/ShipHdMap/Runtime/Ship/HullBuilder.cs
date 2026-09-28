using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShipHdMap
{
    /// The outer shell of a pure car/truck carrier around the decks: a box hull with its bilge, a raked bow with a bulb,
    /// a transom with the stern-ramp opening, the weather deck, the house and bridge forward and the funnel aft. Built
    /// from the same deck data as the interior, so it always fits it. Visual only -- no colliders, not on the
    /// ShipStructure layer: it never catches a placement click and never occludes a sensor sight line.
    ///
    /// Two parts can be hidden for the cutaway view (SetShellMode): the port side and the roof, the two that stand
    /// between the default camera (port quarter, above) and the decks.
    public static class HullBuilder
    {
        public const string Root = "Shell", Port = "ShellPort", Roof = "ShellRoof";
        const float Plate = 0.25f, WaterlineZ = 8.3f, BootTop = 1.2f;

        public static GameObject Build(GameObject ship, IList<Deck> decks, Ramp stern, Dictionary<Color, Material> materials)
        {
            var root = new GameObject(Root); root.transform.SetParent(ship.transform, false);
            root.AddComponent<ShellState>();
            var withOutline = decks.Where(d => d.outline != null && d.outline.Length > 0).ToList();
            if (withOutline.Count == 0) return root;
            double x0 = withOutline.Min(d => d.outline.Min(p => p[0])), x1 = withOutline.Max(d => d.outline.Max(p => p[0]));
            double hb = withOutline.Max(d => d.outline.Max(p => Math.Abs(p[1])));
            var top = withOutline.OrderBy(d => d.z_surface).Last();
            float L = (float)(x1 - x0), cx = (float)(x0 + x1) / 2, B = (float)hb, roofZ = (float)(top.z_surface + top.z_clear) + 0.4f, side = B + Plate / 2, bilge = 2.5f;

            Material M(string hex, float smooth = 0.35f) { var c = Palette.C(hex); if (!materials.TryGetValue(c, out var m)) materials[c] = m = Mats.Lit(c, smooth); return m; }
            var red = M(Palette.HullBottom, 0.2f); var band = M(Palette.HullBand); var white = M(Palette.HullTop, 0.45f);
            var house = M(Palette.Superstructure, 0.45f); var glass = M(Palette.Window, 0.8f); var funnelBand = M(Palette.FunnelBand);

            // topsides in three colour bands, one set per side; port goes under its own parent so cutaway can hide it whole
            var port = Child(root, Port);
            foreach (var (parent, y) in new[] { (root, -side), (port, side) })
            {
                Box(parent, "Bottom", cx, y, bilge, WaterlineZ - BootTop, L, Plate, red);
                Box(parent, "BootTop", cx, y, WaterlineZ - BootTop, WaterlineZ + BootTop, L, Plate, band);
                Box(parent, "Topside", cx, y, WaterlineZ + BootTop, roofZ, L, Plate, white);
                Quad(parent, "Bilge", new Vector3(cx, bilge, (float)-y), new Vector3(L, 0, 0), new Vector3(0, -bilge, Math.Sign(y) * 3f), red);
            }
            // a thin line at every deck above the boot-top and a navy sheer stripe under the roof: without them the
            // topsides are one white slab and nothing says "five decks" from outside
            var line = M(Palette.HullLine);
            foreach (var (parent, y) in new[] { (root, -(side + 0.02f)), (port, side + 0.02f) })
            {
                foreach (var d in withOutline.Where(d => d.z_surface > WaterlineZ + BootTop)) Box(parent, "DeckLine-" + d.id, cx, y, (float)d.z_surface - 0.06f, (float)d.z_surface + 0.06f, L, 0.02f, line);
                Box(parent, "Sheer", cx, y, roofZ - 1.1f, roofZ - 0.5f, L, 0.02f, band);
            }
            Box(root, "Keel", cx, 0, 0, 0.3f, L, (B - 3f) * 2, red);
            var roof = Child(root, Roof);
            Box(roof, "WeatherDeck", cx, 0, roofZ - 0.4f, roofZ, L, B * 2 + Plate * 2, white);

            // transom: closed but for the stern-ramp opening (its width, the ramp deck's clearance)
            float tx = (float)x0 - Plate / 2;
            if (stern != null)
            {
                float oz = (float)stern.hinge[0][2] - 0.2f, oh = (float)(withOutline.OrderBy(d => Math.Abs(d.z_surface - stern.hinge[0][2])).First().z_clear) + 0.2f;
                float ow = (float)stern.width_m / 2;
                Wall(root, "TransomLow", tx, 0, 0.3f, oz, B * 2, red, white);
                Wall(root, "TransomHigh", tx, 0, oz + oh, roofZ, B * 2, red, white);
                Wall(root, "TransomStbd", tx, -(ow + (B - ow) / 2), oz, oz + oh, B - ow, red, white);
                Wall(root, "TransomPort", tx, ow + (B - ow) / 2, oz, oz + oh, B - ow, red, white);
            }
            else Wall(root, "Transom", tx, 0, 0.3f, roofZ, B * 2, red, white);

            // bow: tapered sections from the bow bulkhead forward to a raked stem, and a bulb at the waterline's foot
            var bow = new List<(float x, float hw)> { ((float)x1, B + Plate), ((float)x1 + 6, B * 0.86f), ((float)x1 + 10, B * 0.6f), ((float)x1 + 12.5f, B * 0.28f), ((float)x1 + 13.5f, 0.05f) };
            BowMesh(root, bow, 1.2f, roofZ, red, band, white);
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere); bulb.name = "Bulb";
            UnityEngine.Object.DestroyImmediate(bulb.GetComponent<Collider>());
            bulb.transform.SetParent(root.transform, false);
            bulb.transform.localPosition = new Vector3((float)x1 + 12.5f, 3.2f, 0); bulb.transform.localScale = new Vector3(8f, 3.6f, 3.6f);
            bulb.GetComponent<Renderer>().sharedMaterial = red;

            // house and bridge forward (PCTCs carry them at the bow), funnel aft
            float hx0 = (float)x1 - 18, hx1 = (float)x1 - 2;
            Box(root, "House", (hx0 + hx1) / 2, 0, roofZ, roofZ + 5.2f, hx1 - hx0, B * 1.6f, house);
            Box(root, "HouseWindows", hx1 + 0.02f, 0, roofZ + 2.4f, roofZ + 3.4f, 0.1f, B * 1.5f, glass);
            Box(root, "Wheelhouse", hx1 - 4, 0, roofZ + 5.2f, roofZ + 8f, 7, B * 1.2f, house);
            Box(root, "BridgeWindows", hx1 - 0.48f, 0, roofZ + 6.2f, roofZ + 7.4f, 0.1f, B * 1.15f, glass);
            Box(root, "BridgeWings", hx1 - 2.5f, 0, roofZ + 5.2f, roofZ + 5.6f, 3, B * 2 + 2, house);
            Box(root, "Mast", hx1 - 5, 0, roofZ + 8f, roofZ + 12f, 0.35f, 0.35f, house);
            float fx = (float)x0 + 10;
            Box(root, "Funnel", fx, 0, roofZ, roofZ + 6.5f, 5, 4.2f, house);
            Box(root, "FunnelBand", fx, 0, roofZ + 5f, roofZ + 6.5f, 5.05f, 4.25f, funnelBand);
            return root;
        }

        /// Cutaway hides the port side and the roof -- the parts between the default camera and the decks.
        public static void SetCutaway(GameObject ship, bool cutaway)
        {
            var st = StateOf(ship); if (!st) return;
            st.cutaway = cutaway; Apply(st);
        }

        /// The whole shell on or off (single-deck view hides it: it would stand between the camera and that deck).
        public static void Show(GameObject ship, bool shown)
        {
            var st = StateOf(ship); if (!st) return;
            st.shown = shown; Apply(st);
        }

        static ShellState StateOf(GameObject ship)
        {
            var root = ship ? ship.transform.Find(Root) : null;
            return root ? root.GetComponent<ShellState>() : null;
        }

        static void Apply(ShellState st)
        {
            Transform port = st.transform.Find(Port), roof = st.transform.Find(Roof);
            foreach (var r in st.GetComponentsInChildren<Renderer>(true))
            {
                bool cut = (port && r.transform.IsChildOf(port)) || (roof && r.transform.IsChildOf(roof));
                r.enabled = st.shown && !(st.cutaway && cut);
            }
        }

        static GameObject Child(GameObject parent, string name) { var g = new GameObject(name); g.transform.SetParent(parent.transform, false); return g; }

        /// A quad a-b-c-d seen from both sides. The back face gets its OWN four vertices: sharing them with the front makes
        /// RecalculateNormals average two opposite normals to zero, which lights as NaN -- black on screen, and bloom then
        /// smears that NaN across the whole frame (found in the first browser look at the bow).
        static void TwoSided(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int f = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.AddRange(new[] { f, f + 1, f + 2, f, f + 2, f + 3 });
            int k = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
        }

        /// Axis-aligned box in Ship Frame terms: centre (cx, cy), from z0 to z1, length along x, width along y.
        static GameObject Box(GameObject parent, string name, float cx, float cy, float z0, float z1, float length, float width, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name;
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = ShipFrame.ToUnity(cx, cy, (z0 + z1) / 2);
            g.transform.localScale = new Vector3(length, z1 - z0, width);
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        /// A transverse wall at x, split at the waterline like the sides.
        static void Wall(GameObject parent, string name, float x, float cy, float z0, float z1, float width, Material below, Material above)
        {
            if (z0 < WaterlineZ) Box(parent, name + "Low", x, cy, z0, Mathf.Min(z1, WaterlineZ), Plate, width, below);
            if (z1 > WaterlineZ) Box(parent, name, x, cy, Mathf.Max(z0, WaterlineZ), z1, Plate, width, above);
        }

        /// A single double-sided quad from corner o along a and b (Unity local space).
        static void Quad(GameObject parent, string name, Vector3 centreEdge, Vector3 a, Vector3 b, Material m)
        {
            var o = centreEdge - a / 2;
            var mesh = new Mesh { name = name + "~gen" };
            var v = new List<Vector3>(); var t = new List<int>();
            TwoSided(v, t, o, o + a, o + a + b, o + b);
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var g = Child(parent, name);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = m;
        }

        /// The bow as a lofted skin between plan-view sections (x, half-width), from zBottom to zTop, in three colour bands.
        static void BowMesh(GameObject parent, List<(float x, float hw)> sections, float zBottom, float zTop, Material below, Material band, Material above)
        {
            var bands = new[] { (zBottom, WaterlineZ - BootTop, below), (WaterlineZ - BootTop, WaterlineZ + BootTop, band), (WaterlineZ + BootTop, zTop, above) };
            int k = 0;
            foreach (var (za, zb, mat) in bands)
            {
                var v = new List<Vector3>(); var t = new List<int>();
                foreach (int sgn in new[] { 1, -1 })
                    for (int i = 0; i + 1 < sections.Count; i++)
                    {
                        // the stem rakes forward with height: the lower band is pulled aft a little
                        float rake(float z) => (z - zTop) * 0.12f;
                        var (xa, ha) = sections[i]; var (xb, hbw) = sections[i + 1];
                        TwoSided(v, t, ShipFrame.ToUnity(xa + rake(za), sgn * ha, za), ShipFrame.ToUnity(xb + rake(za), sgn * hbw, za),
                            ShipFrame.ToUnity(xb + rake(zb), sgn * hbw, zb), ShipFrame.ToUnity(xa + rake(zb), sgn * ha, zb));
                    }
                var mesh = new Mesh { name = "Bow" + k + "~gen" };
                mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var g = Child(parent, "Bow" + k++);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            // foredeck over the bow at the roof height
            var top = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i + 1 < sections.Count; i++)
            {
                var (xa, ha) = sections[i]; var (xb, hbw) = sections[i + 1];
                TwoSided(top, tri, ShipFrame.ToUnity(xa, ha, zTop), ShipFrame.ToUnity(xb, hbw, zTop), ShipFrame.ToUnity(xb, -hbw, zTop), ShipFrame.ToUnity(xa, -ha, zTop));
            }
            var deckMesh = new Mesh { name = "Foredeck~gen" };
            deckMesh.SetVertices(top); deckMesh.SetTriangles(tri, 0); deckMesh.RecalculateNormals(); deckMesh.RecalculateBounds();
            var fd = Child(parent, "Foredeck");
            fd.AddComponent<MeshFilter>().sharedMesh = deckMesh;
            fd.AddComponent<MeshRenderer>().sharedMaterial = above;
        }
    }
}
