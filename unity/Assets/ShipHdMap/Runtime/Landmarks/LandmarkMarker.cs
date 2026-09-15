using UnityEngine;

namespace ShipHdMap
{
    /// Scene marker. Unity's Quad renders on its -Z face, so the quad is rotated with LookRotation(-normal)
    /// and the outward normal is -transform.forward.
    public class LandmarkMarker : MonoBehaviour
    {
        public string id; public int code; public float sizeM = 0.3f; public string deckId; public string mountedOn;
        public Vector3 NormalUnity => -transform.forward;

        public static LandmarkMarker Spawn(Transform parent, string id, int code, Vector3 unityPos, Vector3 unityNormal, float sizeM, string deckId, string mountedOn)
        {
            var n = unityNormal.normalized;
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad); g.name = id;
            int layer = LayerMask.NameToLayer("Landmark"); if (layer >= 0) g.layer = layer;
            g.transform.SetParent(parent, false);
            g.transform.position = unityPos + n * 0.01f;
            g.transform.rotation = Quaternion.LookRotation(-n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up);
            g.transform.localScale = new Vector3(sizeM, sizeM, 1);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            var box = g.AddComponent<BoxCollider>(); box.size = new Vector3(1, 1, 0.02f / sizeM);
            var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
            g.GetComponent<Renderer>().sharedMaterial = new Material(shader) { mainTexture = AprilTag36h11.MakeTexture(code) };
            var lm = g.AddComponent<LandmarkMarker>();
            lm.id = id; lm.code = code; lm.sizeM = sizeM; lm.deckId = deckId; lm.mountedOn = mountedOn;
            return lm;
        }

        /// DTO for vehicle-map / API. Position is the mounting point (marker centre pushed back onto the surface).
        public Landmark ToModel()
        {
            var (x, y, z) = ShipFrame.ToShip(transform.position - NormalUnity * 0.01f);
            var (nx, ny, nz) = ShipFrame.ToShip(NormalUnity);
            return new Landmark { id = id, marker = new Marker { family = "apriltag-36h11", code = code },
                position = new[] { x, y, z }, normal = new[] { nx, ny, nz }, size_m = sizeM, deck_id = deckId, mounted_on = mountedOn };
        }
    }
}
