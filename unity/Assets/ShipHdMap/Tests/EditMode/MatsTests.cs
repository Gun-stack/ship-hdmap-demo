using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class MatsTests
    {
        /// The whole point of the templates: a runtime fade must land on the keyword set the build compiled for the
        /// *Fade template. If SetTransparent and the saved template drift apart, the WebGL build strips the variant
        /// the runtime asks for -- and only the browser shows it.
        [Test]
        public void RuntimeFadeMatchesTheSavedTransparentTemplate()
        {
            foreach (var (opaque, fade) in new[] { (Mats.LitName, Mats.LitFadeName), (Mats.UnlitName, Mats.UnlitFadeName) })
            {
                var m = new Material(Mats.Template(opaque));
                Mats.SetFade(m, 0.15f);
                var t = Mats.Template(fade);
                Assert.That(m.shader, Is.EqualTo(t.shader), opaque);
                CollectionAssert.AreEquivalent(t.shaderKeywords, m.shaderKeywords, opaque);
                Assert.That(m.renderQueue, Is.EqualTo(t.renderQueue), opaque);
                Assert.That(m.GetFloat("_Surface"), Is.EqualTo(1f), opaque);
                Mats.SetFade(m, 1f);
                CollectionAssert.AreEquivalent(Mats.Template(opaque).shaderKeywords, m.shaderKeywords, opaque + " back to opaque");
                Object.DestroyImmediate(m);
            }
        }

        [Test]
        public void TemplatesAreUrpShaders()
        {
            foreach (var n in new[] { Mats.LitName, Mats.LitFadeName, Mats.UnlitName, Mats.UnlitFadeName, Mats.UnlitTexName })
                StringAssert.StartsWith("Universal Render Pipeline/", Mats.Template(n).shader.name, n);
        }

        [Test]
        public void ColourGoesToBaseColourAndAlphaPicksTheTemplate()
        {
            var lit = Mats.Lit(Palette.C(Palette.DeckFloor));
            Assert.That(lit.GetColor("_BaseColor"), Is.EqualTo(Palette.C(Palette.DeckFloor)));
            var see = Mats.Unlit(Palette.C(Palette.SlotEmpty, 0.35f));
            Assert.That(see.GetFloat("_Surface"), Is.EqualTo(1f));   // alpha < 1 must come out blended, not opaque
            Assert.That(Mats.Unlit(Palette.C(Palette.Lane)).GetFloat("_Surface"), Is.EqualTo(0f));
            Object.DestroyImmediate(lit); Object.DestroyImmediate(see);
        }

        [Test]
        public void PaletteParsesEveryConstantAndRejectsJunk()
        {
            foreach (var f in typeof(Palette).GetFields())
                if (f.IsLiteral && f.FieldType == typeof(string)) Assert.DoesNotThrow(() => Palette.C((string)f.GetValue(null)), f.Name);
            Assert.Throws<System.ArgumentException>(() => Palette.C("#12"));
        }
    }
}
