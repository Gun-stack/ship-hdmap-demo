using UnityEngine;

namespace ShipHdMap
{
    /// Scene marker. Unity's Quad renders on its -Z face, so the quad is rotated with LookRotation(-normal)
    /// and the outward normal is -transform.forward.
    public class LandmarkMarker : MonoBehaviour
    {
        public string id; public int code; public float sizeM = 0.3f; public string deckId; public string mountedOn;
        public Vector3 NormalUnity => -transform.forward;

        Transform _halo;

        /// Yellow frame behind the tag (4 mm toward the surface, 1.6x) so the selection reads at any camera distance.
        public void SetHighlighted(bool on)
        {
            if (_halo == null)
            {
                if (!on) return;
                var h = GameObject.CreatePrimitive(PrimitiveType.Quad); h.name = "Halo"; h.layer = gameObject.layer;
                Object.DestroyImmediate(h.GetComponent<Collider>());
                h.transform.SetParent(transform, false);
                h.transform.localPosition = new Vector3(0, 0, 0.004f); // marker forward is -normal, so +z is toward the surface
                h.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
                h.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.85f, 0.1f) };
                _halo = h.transform;
            }
            _halo.gameObject.SetActive(on);
        }

        Transform _seenRing;

        /// Green frame behind the tag, smaller than the selection halo so both can show at once:
        /// the halo answers "which one am I editing", this answers "does the sensor have it right now".
        public void SetSeen(bool on)
        {
            if (_seenRing == null)
            {
                if (!on) return;
                var h = GameObject.CreatePrimitive(PrimitiveType.Quad); h.name = "Seen"; h.layer = gameObject.layer;
                Object.DestroyImmediate(h.GetComponent<Collider>());
                h.transform.SetParent(transform, false);
                h.transform.localPosition = new Vector3(0, 0, 0.006f);
                h.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
                h.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.2f, 1f, 0.35f) };
                _seenRing = h.transform;
            }
            _seenRing.gameObject.SetActive(on);
        }

        /// unityPos/unityNormal are in the parent's local space (Ship Frame mapped by ShipFrame.ToUnity); MoveTo takes world.
        public static LandmarkMarker Spawn(Transform parent, string id, int code, Vector3 unityPos, Vector3 unityNormal, float sizeM, string deckId, string mountedOn)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad); g.name = id;
            int layer = LayerMask.NameToLayer("Landmark"); if (layer >= 0) g.layer = layer;
            g.transform.SetParent(parent, false);
            g.transform.localScale = new Vector3(sizeM, sizeM, 1);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            var box = g.AddComponent<BoxCollider>(); box.size = new Vector3(1.6f, 1.6f, 0.02f / sizeM);
            var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
            g.GetComponent<Renderer>().sharedMaterial = new Material(shader) { mainTexture = AprilTag36h11.MakeTexture(code) };
            var lm = g.AddComponent<LandmarkMarker>();
            lm.id = id; lm.code = code; lm.sizeM = sizeM; lm.MoveTo(parent.TransformPoint(unityPos), parent.TransformDirection(unityNormal), deckId, mountedOn);
            return lm;
        }

        /// Puts the tag 1 cm off the surface at unityPos, facing along unityNormal (Quad renders on -Z, so look at -normal).
        public void MoveTo(Vector3 unityPos, Vector3 unityNormal, string deckId, string mountedOn)
        {
            var n = unityNormal.normalized;
            transform.position = unityPos + n * 0.01f;
            transform.rotation = Quaternion.LookRotation(-n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up);
            this.deckId = deckId; this.mountedOn = mountedOn;
        }

        /// DTO for vehicle-map / API, in Ship Frame = the parent's local space. Position is the mounting point (centre pushed back onto the surface).
        public Landmark ToModel()
        {
            Vector3 p = transform.position - NormalUnity * 0.01f, n = NormalUnity;
            var root = transform.parent;
            if (root) { p = root.InverseTransformPoint(p); n = root.InverseTransformDirection(n); }
            var (x, y, z) = ShipFrame.ToShip(p);
            var (nx, ny, nz) = ShipFrame.ToShip(n);
            return new Landmark { id = id, marker = new Marker { family = "apriltag-36h11", code = code },
                position = new[] { x, y, z }, normal = new[] { nx, ny, nz }, size_m = sizeM, deck_id = deckId, mounted_on = mountedOn };
        }
    }
}
