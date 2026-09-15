using UnityEngine;

namespace ShipHdMap
{
    public static class ShipMeshBuilder
    {
        const float FloorThick = 0.2f, WallThick = 0.2f, MepRadius = 0.15f, LashRadius = 0.06f;
        const int MaxLashingPerDeck = 2000; // ponytail: cap for editor perf; instancing/GPU batching if the full grid is ever needed
        static Material _opaque, _fade;

        public static GameObject Build(SeedData seed, ShipParams p, Transform parent = null)
        {
            var ship = new GameObject("Ship"); if (parent) ship.transform.SetParent(parent, false);
            int layer = LayerMask.NameToLayer("ShipStructure"); if (layer < 0) layer = 0;
            foreach (var d in seed.decks)
            {
                var deck = Child(ship, d.id);
                float z = (float)d.z_surface, L = (float)p.lengthM, B = (float)p.beamM;
                var floor = Prim(deck, "Floor", PrimitiveType.Cube, new Vector3(L / 2, z - FloorThick / 2, 0), new Vector3(L, FloorThick, B), Color(0.55f, 0.55f, 0.6f), layer);
                Prim(deck, "HullPort", PrimitiveType.Cube, new Vector3(L / 2, z + (float)d.z_clear / 2, -B / 2), new Vector3(L, (float)d.z_clear, WallThick), Color(0.4f, 0.45f, 0.5f), layer);
                Prim(deck, "HullStbd", PrimitiveType.Cube, new Vector3(L / 2, z + (float)d.z_clear / 2, B / 2), new Vector3(L, (float)d.z_clear, WallThick), Color(0.4f, 0.45f, 0.5f), layer);
                var pillars = Child(deck, "Pillars");
                foreach (var f in seed.facilities) if (f.deck_id == d.id && f.kind == "pillar")
                {
                    float cx = (float)(f.footprint[0][0] + f.footprint[2][0]) / 2, cy = (float)(f.footprint[0][1] + f.footprint[2][1]) / 2;
                    float sx = (float)(f.footprint[2][0] - f.footprint[0][0]), sy = (float)(f.footprint[2][1] - f.footprint[0][1]), h = (float)(f.z_max - f.z_min);
                    Prim(pillars, f.id, PrimitiveType.Cube, ShipFrame.ToUnity(cx, cy, f.z_min + h / 2), new Vector3(sx, h, sy), Color(0.35f, 0.35f, 0.38f), layer);
                }
                var lash = Child(deck, "Lashing"); int n = 0;
                foreach (var lp in seed.lashing_points) if (lp.deck_id == d.id && n++ < MaxLashingPerDeck)
                {
                    var g = Prim(lash, lp.id, PrimitiveType.Cylinder, ShipFrame.ToUnity(lp.position[0], lp.position[1], lp.position[2] + 0.005), new Vector3(LashRadius * 2, 0.005f, LashRadius * 2), Color(0.8f, 0.2f, 0.2f), layer);
                    Object.DestroyImmediate(g.GetComponent<Collider>()); // sockets are visual only
                }
                var mep = Child(deck, "MEP");
                foreach (float y in new[] { -8f, 0f, 8f })
                {
                    var pipe = Prim(mep, $"Pipe_{y:+0;-0;0}", PrimitiveType.Cylinder, ShipFrame.ToUnity(p.lengthM / 2, y, d.z_surface + d.z_clear - 0.3), new Vector3(MepRadius * 2, L / 2, MepRadius * 2), Color(0.75f, 0.6f, 0.2f), layer);
                    pipe.transform.rotation = Quaternion.Euler(0, 0, 90); // cylinder axis along Unity X
                }
            }
            foreach (var r in seed.ramps)
            {
                var ramp = Child(ship, "Ramp");
                ramp.transform.position = ShipFrame.ToUnity((r.hinge[0][0] + r.hinge[1][0]) / 2, (r.hinge[0][1] + r.hinge[1][1]) / 2, r.hinge[0][2]);
                float len = (float)r.length_m, w = (float)r.width_m;
                Prim(ramp, "Plate", PrimitiveType.Cube, ramp.transform.position + new Vector3(-len / 2, -FloorThick / 2, 0), new Vector3(len, FloorThick, w), Color(0.5f, 0.5f, 0.45f), layer);
            }
            Physics.SyncTransforms(); // project has autoSyncTransforms off; Collider.bounds needs a manual sync after transform edits
            return ship;
        }

        /// Positive angle lifts the free (stern, -x) end. Hinge line is along Unity Z, so rotate about Z.
        public static void SetRampAngle(GameObject ship, double angleDeg)
        {
            var ramp = ship.transform.Find("Ramp"); if (!ramp) return;
            ramp.rotation = Quaternion.Euler(0, 0, (float)-angleDeg);
            Physics.SyncTransforms();
        }

        public static void SetDeckVisibility(GameObject ship, string deckIdOrAll)
        {
            foreach (Transform deck in ship.transform)
            {
                if (deck.name == "Ramp") continue;
                bool visible = deckIdOrAll == "all" || deck.name == deckIdOrAll;
                foreach (var r in deck.GetComponentsInChildren<Renderer>()) SetAlpha(r, visible ? 1f : 0.15f);
            }
        }

        static GameObject Child(GameObject parent, string name) { var g = new GameObject(name); g.transform.SetParent(parent.transform, false); return g; }

        static GameObject Prim(GameObject parent, string name, PrimitiveType t, Vector3 pos, Vector3 scale, Color c, int layer)
        {
            var g = GameObject.CreatePrimitive(t); g.name = name; g.layer = layer; g.transform.SetParent(parent.transform, false);
            g.transform.position = pos; g.transform.localScale = scale;
            var mat = new Material(Shader.Find("Standard")) { color = c }; g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        static Color Color(float r, float g, float b) => new Color(r, g, b, 1f);

        static void SetAlpha(Renderer r, float a)
        {
            var m = r.sharedMaterial; var c = m.color; c.a = a; m.color = c;
            if (a < 1f) { m.SetFloat("_Mode", 2); m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0); m.EnableKeyword("_ALPHABLEND_ON"); m.renderQueue = 3000; }
            else { m.SetFloat("_Mode", 0); m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero); m.SetInt("_ZWrite", 1); m.DisableKeyword("_ALPHABLEND_ON"); m.renderQueue = -1; }
        }
    }
}
