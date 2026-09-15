using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class ShipFrameTests
    {
        static JObject Vectors()
        {
            // unity/ 에서 두 단계 위가 저장소 루트
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "test-vectors", "ship-frame.json"));
            return JObject.Parse(File.ReadAllText(path));
        }

        [Test]
        public void PointsRoundTrip()
        {
            foreach (var p in Vectors()["points"])
            {
                var s = p["ship"]; var u = p["unity"];
                Vector3 got = ShipFrame.ToUnity((double)s[0], (double)s[1], (double)s[2]);
                Assert.That(got.x, Is.EqualTo((float)u[0]).Within(1e-5), p.ToString());
                Assert.That(got.y, Is.EqualTo((float)u[1]).Within(1e-5), p.ToString());
                Assert.That(got.z, Is.EqualTo((float)u[2]).Within(1e-5), p.ToString());
                var back = ShipFrame.ToShip(got);
                Assert.That(back.x, Is.EqualTo((double)s[0]).Within(1e-5));
                Assert.That(back.y, Is.EqualTo((double)s[1]).Within(1e-5));
                Assert.That(back.z, Is.EqualTo((double)s[2]).Within(1e-5));
            }
        }

        [Test]
        public void HeadingsMatchVectors()
        {
            foreach (var h in Vectors()["headings"])
            {
                double deg = (double)h["heading_deg"];
                Assert.That(ShipFrame.UnityYawDeg(deg), Is.EqualTo((float)h["unity_yaw_deg"]).Within(1e-5));
                Vector3 dir = ShipFrame.HeadingVector(deg);
                var e = h["unity_dir"];
                Assert.That(dir.x, Is.EqualTo((float)e[0]).Within(1e-5));
                Assert.That(dir.y, Is.EqualTo((float)e[1]).Within(1e-5));
                Assert.That(dir.z, Is.EqualTo((float)e[2]).Within(1e-5));
                // Quaternion 으로 +X 를 돌린 결과가 HeadingVector 와 같아야 한다
                Vector3 viaQuat = Quaternion.Euler(0, ShipFrame.UnityYawDeg(deg), 0) * Vector3.right;
                Assert.That(Vector3.Distance(viaQuat, dir), Is.LessThan(1e-4));
            }
        }

        [Test]
        public void WrapDeg()
        {
            foreach (var w in Vectors()["wrap_deg"])
                Assert.That(ShipFrame.WrapDeg((double)w["in"]), Is.EqualTo((double)w["out"]).Within(1e-9));
            Assert.That(ShipFrame.WrapRad(System.Math.PI * 3), Is.EqualTo(System.Math.PI).Within(1e-12));
            Assert.That(ShipFrame.WrapRad(-System.Math.PI), Is.EqualTo(System.Math.PI).Within(1e-12));
        }
    }
}
