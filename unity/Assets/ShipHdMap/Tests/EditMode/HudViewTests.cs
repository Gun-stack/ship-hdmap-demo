using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShipHdMap.Tests
{
    public class HudViewTests
    {
        [Test]
        public void LinesFormatContextCursorAndEstimate()
        {
            var idle = HudView.Lines("D3", "LM-0001", (48.094, 6.81, 11.67), null, default, "SHIP_AP");
            Assert.That(idle.Length, Is.EqualTo(2));
            Assert.That(idle[0], Is.EqualTo("deck D3   sel LM-0001"));
            Assert.That(idle[1], Does.StartWith("cursor x   48.09"));
            var r = new LocalizerResult { ok = true, pose = new Pose2D { x = 1.85, y = -0.14, psiRad = 0.8 * System.Math.PI / 180 }, nObs = 2, residualRms = 0.031, iterations = 3 };
            var drive = HudView.Lines("all", null, null, r, new Pose2D { x = 2, y = 0, psiRad = 0 }, "SHIP_AP");
            Assert.That(drive.Length, Is.EqualTo(6));
            Assert.That(drive[1], Is.EqualTo("cursor -"));
            Assert.That(drive[2], Does.Contain("N 2")); Assert.That(drive[2], Does.Contain("RMS 0.031"));
            Assert.That(drive[5], Does.StartWith("err  0.21 m"));
        }

        [Test]
        public void HudAssetsExistInResources()
        {
            var ps = Resources.Load<PanelSettings>("HudPanelSettings");
            Assert.That(ps, Is.Not.Null, "run menu ShipHdMap/Create HUD Assets");
            Assert.That(ps.themeStyleSheet, Is.Not.Null);
        }
    }
}
