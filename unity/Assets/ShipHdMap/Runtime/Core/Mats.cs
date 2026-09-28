using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShipHdMap
{
    /// Material factory for the URP scene. Every call returns a NEW Material; callers keep their own caches, exactly
    /// as they did when each built `new Material(Shader.Find(...))` itself.
    ///
    /// Why templates instead of Shader.Find: URP strips every shader keyword combination that no material in the
    /// build uses. A material that only exists at runtime and flips itself transparent asks for a variant the WebGL
    /// build never compiled, and renders opaque or not at all -- in the browser only, never in the editor.
    /// Editor/RenderSetup saves one template per surface kind under Resources/Materials (so the build keeps both
    /// the shader and those variants), and everything here clones one of them.
    public static class Mats
    {
        public const string LitName = "Lit", LitFadeName = "LitFade", UnlitName = "Unlit", UnlitFadeName = "UnlitFade", UnlitTexName = "UnlitTex";
        public const string LitShader = "Universal Render Pipeline/Lit", UnlitShader = "Universal Render Pipeline/Unlit";
        static readonly Dictionary<string, Material> Templates = new();

        public static Material Template(string name)
        {
            if (Templates.TryGetValue(name, out var t) && t) return t;
            t = Resources.Load<Material>("Materials/" + name);
            if (!t) throw new System.InvalidOperationException($"Resources/Materials/{name} missing -- run menu ShipHdMap/Setup Rendering");
            return Templates[name] = t;
        }

        public static Material Lit(Color c, float smoothness = 0.3f, float metallic = 0f, string name = null)
        {
            var m = Clone(LitName, name); m.color = c;
            m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", metallic);
            return m;
        }

        public static Material Unlit(Color c, string name = null) { var m = Clone(c.a < 1f ? UnlitFadeName : UnlitName, name); m.color = c; return m; }

        public static Material Textured(Texture tex, string name = null) { var m = Clone(UnlitTexName, name); m.mainTexture = tex; return m; }

        /// Fade a Lit (or Unlit) material in place: below 1 it becomes alpha-blended, at 1 it is opaque again.
        public static void SetFade(Material m, float alpha)
        {
            var c = m.color; c.a = alpha; m.color = c;
            SetTransparent(m, alpha < 1f);
        }

        /// The URP surface switch the material inspector would make, done by hand because BaseShaderGUI is editor-only.
        /// RenderSetup builds the *Fade templates with this same function, so a runtime switch lands on exactly the
        /// keyword set the build compiled for (MatsTests pins that).
        public static void SetTransparent(Material m, bool on)
        {
            m.SetFloat("_Surface", on ? 1 : 0);
            m.SetFloat("_Blend", 0);   // alpha
            // Off, or URP's material validation (run when the template is saved) switches "alpha" to premultiplied and adds
            // _ALPHAPREMULTIPLY_ON -- a keyword this runtime switch would not set, so it would ask the build for a variant
            // nothing compiled. MatsTests caught exactly that.
            m.SetFloat("_BlendModePreserveSpecular", 0);
            m.SetFloat("_SrcBlend", (float)(on ? BlendMode.SrcAlpha : BlendMode.One));
            m.SetFloat("_DstBlend", (float)(on ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)(on ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            m.SetFloat("_ZWrite", on ? 0 : 1);
            m.SetOverrideTag("RenderType", on ? "Transparent" : "Opaque");
            if (on) m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHAMODULATE_ON");   // what URP's own validation leaves for "alpha" without preserve-specular
            m.renderQueue = on ? (int)RenderQueue.Transparent : -1;
            m.SetShaderPassEnabled("ShadowCaster", !on);   // a faded deck must not darken the deck you are looking at
        }

        static Material Clone(string template, string name)
        {
            var m = new Material(Template(template));
            m.name = name ?? template;
            return m;
        }
    }
}
