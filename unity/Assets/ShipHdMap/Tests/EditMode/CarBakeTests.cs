using System.IO;
using NUnit.Framework;
using ShipHdMap.Editor;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class CarBakeTests
    {
        static string Obj() => File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Art/Vehicles/hatchback-sports.obj"));

        [Test]
        public void FitsTheCarClassFootprintWithItsOriginOnTheDeck()
        {
            var m = CarBake.Build(Obj());
            var b = m.bounds;
            Assert.That(b.size.x, Is.EqualTo(CarModel.LengthM).Within(1e-3));
            Assert.That(b.size.y, Is.EqualTo(CarModel.HeightM).Within(1e-3));
            Assert.That(b.size.z, Is.EqualTo(CarModel.WidthM).Within(1e-3));
            Assert.That(b.min.y, Is.EqualTo(0f).Within(1e-4));             // origin on the ground: parked at the deck surface, not in it
            Assert.That(b.center.x, Is.EqualTo(0f).Within(1e-3)); Assert.That(b.center.z, Is.EqualTo(0f).Within(1e-3));
            Object.DestroyImmediate(m);
        }

        [Test]
        public void PaintIsItsOwnSubmeshAndABigShareOfTheBody()
        {
            var m = CarBake.Build(Obj());
            Assert.That(m.subMeshCount, Is.EqualTo(2));
            // By AREA, not triangle count: the wheels are most of the triangles and almost none of the car.
            // If PaintUv ever stops matching the kit's body swatch, paint collapses to 0 and every car is one colour map.
            float paint = Area(m, 0), other = Area(m, 1);
            Assert.That(paint / (paint + other), Is.InRange(0.2f, 0.6f));
            Object.DestroyImmediate(m);
        }

        static float Area(Mesh m, int sub)
        {
            var v = m.vertices; var t = m.GetTriangles(sub); float a = 0;
            for (int i = 0; i < t.Length; i += 3) a += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude / 2;
            return a;
        }

        [Test]
        public void FacesPlusX()
        {
            // VehicleController drives the body along its local +X. The kit's headlights sample the yellow swatch
            // (column 3, v < 0.25) and its tail lights the red one (column 5, v < 0.25); after the re-axis the first
            // must sit forward of the second, or every car on the ship reverses into its slot.
            var m = CarBake.Build(Obj());
            var v = m.vertices; var uv = m.uv;
            var head = new Rect(3 / 16f, 0f, 1 / 16f, 0.25f); var tail = new Rect(5 / 16f, 0f, 1 / 16f, 0.25f);
            float hx = 0, tx = 0; int hn = 0, tn = 0;
            for (int i = 0; i < v.Length; i++) { if (head.Contains(uv[i])) { hx += v[i].x; hn++; } if (tail.Contains(uv[i])) { tx += v[i].x; tn++; } }
            Assert.That(hn, Is.GreaterThan(0)); Assert.That(tn, Is.GreaterThan(0));
            Assert.That(hx / hn, Is.GreaterThan(1.5f));    // near the nose
            Assert.That(tx / tn, Is.LessThan(-1.5f));      // near the tail
            Object.DestroyImmediate(m);
        }

        [Test]
        public void FacesPointTheWayTheirNormalsDo()
        {
            // Unity draws a triangle's front where Cross(b - a, c - a) points. If that disagrees with the authored
            // normals, back-face culling shows the inside of the car: from above you see its floor and wheels.
            var m = CarBake.Build(Obj());
            var v = m.vertices; var n = m.normals; int agree = 0, total = 0;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var t = m.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    var g = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    if (g.sqrMagnitude < 1e-12f) continue;
                    total++; if (Vector3.Dot(g, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]) > 0) agree++;
                }
            }
            Assert.That(agree / (float)total, Is.GreaterThan(0.95f));
            Object.DestroyImmediate(m);
        }

        [Test]
        public void TheBakedAssetShipsInResources() => Assert.That(Resources.Load<Mesh>("Vehicle/Car"), Is.Not.Null, "run menu ShipHdMap/Bake Car Mesh");
    }
}
