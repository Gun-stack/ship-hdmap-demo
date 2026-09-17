using UnityEngine;

namespace ShipHdMap
{
    /// The quay slab. Not map data (the HD map is the ship's), so it lives in world space -- the Quay Frame -- and is
    /// never parented to the Map root: pose moves the ship, not the quay. Spec M5b §2.3.
    public static class QuayBuilder
    {
        public const double LengthM = 60, WidthM = 30, ThickM = 0.4;
        static Material _mat;

        /// Slab astern of the AP: ship -x, i.e. Unity x in [-LengthM, 0].
        public static GameObject Build()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Quay";
            Object.DestroyImmediate(g.GetComponent<Collider>());   // never catches a placement raycast or a sensor linecast
            g.transform.localScale = new Vector3((float)LengthM, (float)ThickM, (float)WidthM);
            if (!_mat) _mat = new Material(Shader.Find("Standard")) { color = new Color(0.62f, 0.6f, 0.56f), name = "quay" };
            g.GetComponent<Renderer>().sharedMaterial = _mat;
            SetHeight(g, 0);
            return g;
        }

        /// surfaceZ: the quay surface in the Quay Frame, i.e. quay_z_m + tide_m above the waterline.
        public static void SetHeight(GameObject quay, double surfaceZ)
        {
            if (!quay) return;
            quay.transform.position = new Vector3(-(float)LengthM / 2, (float)(surfaceZ - ThickM / 2), 0);
        }

        public static double SurfaceZ(GameObject quay) => quay.transform.position.y + ThickM / 2;
    }
}
