using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// The one passenger-car model (Kenney Car Kit, CC0 -- unity/Art/Vehicles), for both the driven vehicle and the
    /// parked ones. Editor/CarBake turns the OBJ into Resources/Vehicle/Car: forward = +X, origin = the centre of the
    /// footprint ON THE DECK, fitted to the car class's 4.8 x 1.5 x 1.85 m. Submesh 0 is the body paint (coloured
    /// from Palette per use), submesh 1 everything else (glass, tyres, lights) textured from the kit's colour map.
    public static class CarModel
    {
        public const float LengthM = 4.8f, HeightM = 1.5f, WidthM = 1.85f;
        static Mesh _mesh; static Material _detail;
        static readonly Dictionary<Color, Material> Paint = new();   // shared across Loads, like MapOverlay's line colours

        /// Gives `go` a MeshFilter + MeshRenderer carrying the car. A single renderer on `go` itself, on purpose:
        /// MapOverlay's deck filter and the tests read `GetComponent<Renderer>()` on PARKED-* directly.
        public static MeshRenderer Dress(GameObject go, Color paint)
        {
            if (!_mesh) _mesh = Resources.Load<Mesh>("Vehicle/Car");
            if (!_mesh) throw new System.InvalidOperationException("Resources/Vehicle/Car missing -- run menu ShipHdMap/Bake Car Mesh");
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[] { PaintMat(paint), DetailMat() };
            return r;
        }

        static Material PaintMat(Color c)
        {
            if (Paint.TryGetValue(c, out var m) && m) return m;
            return Paint[c] = Mats.Lit(c, 0.7f, 0.35f, "car-paint");
        }

        static Material DetailMat()
        {
            if (_detail) return _detail;
            _detail = Mats.Lit(Color.white, 0.35f, 0f, "car-detail");
            _detail.mainTexture = Resources.Load<Texture2D>("Vehicle/colormap");
            return _detail;
        }
    }
}
