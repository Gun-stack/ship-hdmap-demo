using UnityEngine;

namespace ShipHdMap
{
    /// The quay slab. Not map data (the HD map is the ship's), so it lives in world space -- the Quay Frame -- and is
    /// never parented to the Map root: pose moves the ship, not the quay. Spec M5b §2.3.
    public static class QuayBuilder
    {
        public const double LengthM = 60, WidthM = 30, ThickM = 0.4;
        static Material _mat;

        /// Slab astern of the ramp foot; Place() puts its shipward edge there.
        public static GameObject Build()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Quay";
            Object.DestroyImmediate(g.GetComponent<Collider>());   // never catches a placement raycast or a sensor linecast
            g.transform.localScale = new Vector3((float)LengthM, (float)ThickM, (float)WidthM);
            if (!_mat) _mat = new Material(Shader.Find("Standard")) { color = new Color(0.62f, 0.6f, 0.56f), name = "quay" };
            g.GetComponent<Renderer>().sharedMaterial = _mat;
            Place(g, 0, 0);
            return g;
        }

        /// surfaceZ: the quay surface in the Quay Frame, i.e. quay_z_m + tide_m above the waterline.
        /// nearX: where the slab's shipward edge stops -- the ramp's foot, so the slab occupies [nearX - LengthM, nearX].
        /// It must NOT run on to the AP: whenever the quay surface sits above the hinge (hinge_z - draft_aft -- which
        /// the demo's default pose does) the ramp descends from the quay to the ship, so its whole surface lies below
        /// the slab's top face and a slab reaching the AP buries the ramp end to end: it vanishes and the car drives
        /// in through solid concrete.
        public static void Place(GameObject quay, double surfaceZ, double nearX)
        {
            if (!quay) return;
            quay.transform.position = new Vector3((float)(nearX - LengthM / 2), (float)(surfaceZ - ThickM / 2), 0);
        }

        public static double SurfaceZ(GameObject quay) => quay.transform.position.y + ThickM / 2;
    }
}
