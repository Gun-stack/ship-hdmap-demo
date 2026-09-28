using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShipHdMap.Editor
{
    /// Everything the URP scene needs that has to exist as an ASSET or be SAVED IN THE SCENE, built from code so it can
    /// be re-run and reviewed. Idempotent: existing assets are reused and re-tuned, never duplicated.
    ///
    /// Why not set these at runtime: a WebGL build keeps only the shader variants some build-time asset asks for.
    /// Fog set from a script at runtime has no fog variant to draw with, a material made transparent at runtime has no
    /// transparent variant, and a Volume profile made at runtime has its post-processing variants stripped. Saving
    /// the fog in the scene, the templates in Resources and the profile as an asset is what keeps them.
    ///
    /// Batch: Unity -batchmode -projectPath unity -executeMethod ShipHdMap.Editor.RenderSetup.Run -quit
    public static class RenderSetup
    {
        const string Settings = "Assets/ShipHdMap/Settings", MatDir = "Assets/ShipHdMap/Resources/Materials";
        const string UrpPath = Settings + "/URP.asset", RendererPath = Settings + "/URP_Renderer.asset";
        const string ProfilePath = Settings + "/SceneVolume.asset", SkyPath = Settings + "/Sky.mat", WaterPath = Settings + "/Water.mat";
        const string ScenePath = "Assets/Scenes/Demo.unity";

        [MenuItem("ShipHdMap/Setup Rendering")]
        public static void Run()
        {
            Folder(Settings); Folder(MatDir);
            CarBake.Bake();
            var urp = PipelineAsset();
            AssignPipeline(urp);
            Templates();
            var profile = Profile();
            var sky = SkyMaterial();
            var water = WaterMaterial();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ApplySceneLook(profile, sky, water);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[RenderSetup] pipeline={GraphicsSettings.defaultRenderPipeline?.name} profile overrides={profile.components.Count}");
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static UniversalRenderPipelineAsset PipelineAsset()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath);
            if (urp == null)
            {
                var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                // what URP's own "URP Asset (with Universal Renderer)" menu wires in; without it post-processing is silently off
                rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");
                AssetDatabase.CreateAsset(rd, RendererPath);
                urp = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(urp, UrpPath);
            }
            var so = new SerializedObject(urp);
            so.FindProperty("m_SupportsHDR").boolValue = true;
            so.FindProperty("m_MSAA").intValue = 4;                   // thin LineRenderers (lanes, slot outlines, the cone) need it most
            so.FindProperty("m_RenderScale").floatValue = 1f;
            so.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 2048;
            so.FindProperty("m_ShadowDistance").floatValue = 160f;     // a deck is ~150 m long; the orbit camera frames most of one
            so.FindProperty("m_ShadowCascadeCount").intValue = 2;
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
            return urp;
        }

        static void AssignPipeline(UniversalRenderPipelineAsset urp)
        {
            GraphicsSettings.defaultRenderPipeline = urp;
            // Every quality level explicitly, the way URP's own Rendering Settings converter does it: a level left
            // empty silently inherits, and one still pointing at nothing would be Built-in on that level.
            int keep = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = urp; }
            QualitySettings.SetQualityLevel(keep, false);
            // the Built-in shaders WebGLBuild used to force-include: dead weight under URP
            foreach (var name in new[] { "Standard", "Unlit/Texture", "Unlit/Color" }) WebGLBuild.ExcludeShader(name);
        }

        static void Templates()
        {
            Template(Mats.LitName, Mats.LitShader, false);
            Template(Mats.LitFadeName, Mats.LitShader, true);
            Template(Mats.UnlitName, Mats.UnlitShader, false);
            Template(Mats.UnlitFadeName, Mats.UnlitShader, true);
            Template(Mats.UnlitTexName, Mats.UnlitShader, false);
        }

        static Material Template(string name, string shader, bool transparent)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find(shader);
            Mats.SetTransparent(m, transparent);
            EditorUtility.SetDirty(m);
            return m;
        }

        static VolumeProfile Profile()
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (p == null) { p = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(p, ProfilePath); }

            var tone = Override<Tonemapping>(p);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = Override<Bloom>(p);
            bloom.threshold.Override(1.0f); bloom.intensity.Override(0.35f); bloom.scatter.Override(0.6f);
            var color = Override<ColorAdjustments>(p);
            color.postExposure.Override(0f); color.contrast.Override(8f); color.saturation.Override(6f);
            var vignette = Override<Vignette>(p);
            vignette.intensity.Override(0.22f); vignette.smoothness.Override(0.45f);

            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            // A profile whose overrides were Add()ed but not saved as sub-assets reloads as `components: [ {fileID: 0} ]`
            // and renders nothing -- check the saved state, not the call we just made.
            int live = 0; foreach (var c in p.components) if (c != null) live++;
            if (live < 4) throw new System.Exception($"[RenderSetup] {ProfilePath} kept {live} of 4 overrides");
            return p;
        }

        static T Override<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (p.TryGet<T>(out var have) && have != null) return have;
            var c = p.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, p);
            EditorUtility.SetDirty(c);
            return c;
        }

        static Material SkyMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (m == null) { m = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(m, SkyPath); }
            m.SetFloat("_SunSize", 0.035f); m.SetFloat("_AtmosphereThickness", 0.85f); m.SetFloat("_Exposure", 1.15f);
            m.SetColor("_SkyTint", new Color(0.52f, 0.58f, 0.66f));
            m.SetColor("_GroundColor", Palette.C(Palette.Water));
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material WaterMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(WaterPath);
            if (m == null) { m = new Material(Shader.Find(Mats.LitShader)); AssetDatabase.CreateAsset(m, WaterPath); }
            m.shader = Shader.Find(Mats.LitShader);
            m.color = Palette.C(Palette.Water, 0.82f);   // see-through enough that the decks under the waterline still read
            m.SetFloat("_Smoothness", 0.92f); m.SetFloat("_Metallic", 0f);
            Mats.SetTransparent(m, true);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// Camera, sun, sky, fog, post-processing volume and the sea, on whatever scene is open. SceneSetup calls this
        /// too, so a freshly generated Demo scene comes out looking the same.
        public static void ApplySceneLook(VolumeProfile profile, Material sky, Material water)
        {
            var cam = Camera.main;
            if (cam)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.farClipPlane = 3000f;
                // explicit checks, not ??: in the editor a missing component comes back as a fake null that ?? does not see
                var data = cam.GetComponent<UniversalAdditionalCameraData>(); if (!data) data = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.None;   // MSAA x4 from the pipeline asset already does the edges
                data.renderShadows = true;
                EditorUtility.SetDirty(data); EditorUtility.SetDirty(cam);
            }

            var sun = Object.FindFirstObjectByType<Light>();
            if (sun && sun.type == LightType.Directional)
            {
                sun.transform.rotation = Quaternion.Euler(48f, -38f, 0f);   // from astern-starboard, so pillars throw shadows across the lanes
                sun.color = new Color(1f, 0.95f, 0.88f);
                sun.intensity = 1.3f;
                sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.8f; sun.shadowBias = 0.05f; sun.shadowNormalBias = 0.4f;
                EditorUtility.SetDirty(sun);
                RenderSettings.sun = sun;
            }

            RenderSettings.skybox = sky;
            // Trilight, not Skybox: a skybox ambient probe is only computed by a lighting bake, and this scene has none.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.76f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.48f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.2f, 0.23f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.66f, 0.72f, 0.79f);
            RenderSettings.fogStartDistance = 220f; RenderSettings.fogEndDistance = 1400f;

            var fx = GameObject.Find("PostFX"); if (!fx) fx = new GameObject("PostFX");
            var vol = fx.GetComponent<Volume>(); if (!vol) vol = fx.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 0; vol.weight = 1f; vol.sharedProfile = profile;
            EditorUtility.SetDirty(vol);

            var sea = GameObject.Find("Sea");
            if (!sea)
            {
                sea = GameObject.CreatePrimitive(PrimitiveType.Quad); sea.name = "Sea";
                Object.DestroyImmediate(sea.GetComponent<Collider>());   // must never catch a placement raycast or a sensor linecast
            }
            // World y = 0 is the waterline: QuayBuilder puts the quay surface at quay_z + tide above it, and pose sinks the keel by the draft.
            sea.transform.SetPositionAndRotation(new Vector3(0, -0.05f, 0), Quaternion.Euler(90f, 0f, 0f));
            sea.transform.localScale = new Vector3(6000f, 6000f, 1f);
            var r = sea.GetComponent<MeshRenderer>();
            r.sharedMaterial = water; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
            EditorUtility.SetDirty(sea);
        }
    }
}
