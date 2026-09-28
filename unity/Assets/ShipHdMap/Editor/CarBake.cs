using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShipHdMap.Editor
{
    /// Kenney's hatchback-sports.obj -> Resources/Vehicle/Car.asset, in the shape CarModel promises. The OBJ lives
    /// outside Assets (unity/Art/Vehicles) so Unity never imports it as a model with its own materials.
    public static class CarBake
    {
        const string Obj = "Art/Vehicles/hatchback-sports.obj";           // relative to the project root (unity/)
        const string MeshPath = "Assets/ShipHdMap/Resources/Vehicle/Car.asset", TexPath = "Assets/ShipHdMap/Resources/Vehicle/colormap.png";

        /// The body paint is one swatch of the kit's 16-column colour map: column 7, v 0.5..0.75 (the green in the
        /// kit's own preview). Everything the model paints from that cell becomes submesh 0 so it can be recoloured.
        /// Found by sampling the map at every body triangle's UV and summing area: this cell and the window cell
        /// just below it are the two big ones, and the windows are the grey-blue.
        public static readonly Rect PaintUv = new(7 / 16f, 0.5f, 1 / 16f, 0.25f);

        [MenuItem("ShipHdMap/Bake Car Mesh")]
        public static void Bake()
        {
            var mesh = Build(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), Obj)));
            mesh.name = "Car";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (old) { EditorUtility.CopySerialized(mesh, old); EditorUtility.SetDirty(old); } else AssetDatabase.CreateAsset(mesh, MeshPath);
            // Swatches sit side by side in one map: mips would bleed neighbours into each other at a distance.
            if (AssetImporter.GetAtPath(TexPath) is TextureImporter ti)
            {
                ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear; ti.sRGBTexture = true;
                ti.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[CarBake] {MeshPath}: {mesh.vertexCount} verts, bounds {mesh.bounds.size}, paint tris {mesh.GetTriangles(0).Length / 3}, other {mesh.GetTriangles(1).Length / 3}");
        }

        /// Parse, re-axis, fit, split. OBJ is right-handed, y up, and this kit faces +z; the car class faces Unity +X.
        /// Mapping obj (x, y, z) -> Unity (z, y, x) turns it to face +X and flips handedness in one reflection -- and
        /// the reflection mirrors each triangle's winding too, so every face is emitted in reverse order. (The first
        /// bake kept the OBJ order on the theory that the reflection alone would fix it: the car rendered inside out,
        /// wheels up. CarBakeTests.FacesPointTheWayTheirNormalsDo pins it now.)
        public static Mesh Build(string objText)
        {
            var v = new List<Vector3>(); var vt = new List<Vector2>(); var vn = new List<Vector3>();
            var pos = new List<Vector3>(); var uv = new List<Vector2>(); var nrm = new List<Vector3>();
            var paint = new List<int>(); var other = new List<int>();
            float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
            foreach (var raw in objText.Split('\n'))
            {
                var t = raw.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0) continue;
                switch (t[0])
                {
                    case "v": v.Add(new Vector3(F(t[3]), F(t[2]), F(t[1]))); break;
                    case "vt": vt.Add(new Vector2(F(t[1]), F(t[2]))); break;
                    case "vn": vn.Add(new Vector3(F(t[3]), F(t[2]), F(t[1]))); break;
                    case "f":
                        var corner = new int[t.Length - 1]; var uvSum = Vector2.zero;
                        for (int i = 1; i < t.Length; i++)
                        {
                            var ix = t[i].Split('/');
                            pos.Add(v[int.Parse(ix[0]) - 1]);
                            var u = ix.Length > 1 && ix[1] != "" ? vt[int.Parse(ix[1]) - 1] : Vector2.zero; uv.Add(u); uvSum += u;
                            nrm.Add(ix.Length > 2 && ix[2] != "" ? vn[int.Parse(ix[2]) - 1] : Vector3.up);
                            corner[i - 1] = pos.Count - 1;
                        }
                        var into = PaintUv.Contains(uvSum / corner.Length) ? paint : other;
                        for (int i = 1; i + 1 < corner.Length; i++) { into.Add(corner[0]); into.Add(corner[i + 1]); into.Add(corner[i]); }   // fan, reversed
                        break;
                }
            }

            var b = new Bounds(pos[0], Vector3.zero); foreach (var p in pos) b.Encapsulate(p);
            var k = new Vector3(CarModel.LengthM / b.size.x, CarModel.HeightM / b.size.y, CarModel.WidthM / b.size.z);
            var origin = new Vector3(b.center.x, b.min.y, b.center.z);   // footprint centre, on the ground
            for (int i = 0; i < pos.Count; i++) pos[i] = Vector3.Scale(pos[i] - origin, k);
            // A non-uniform stretch bends normals: transform by the inverse scale and renormalise.
            var inv = new Vector3(1 / k.x, 1 / k.y, 1 / k.z);
            for (int i = 0; i < nrm.Count; i++) nrm[i] = Vector3.Scale(nrm[i], inv).normalized;

            var mesh = new Mesh { indexFormat = pos.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(pos); mesh.SetUVs(0, uv); mesh.SetNormals(nrm);
            mesh.subMeshCount = 2; mesh.SetTriangles(paint, 0); mesh.SetTriangles(other, 1);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }
    }
}
