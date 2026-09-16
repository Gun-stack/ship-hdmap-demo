using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShipHdMap.Editor
{
    /// Creates the runtime UI Toolkit assets the HUD needs (a PanelSettings with the default theme) under Resources/. Idempotent.
    public static class HudAssets
    {
        const string Dir = "Assets/ShipHdMap/Resources", Tss = Dir + "/HudTheme.tss", Ps = Dir + "/HudPanelSettings.asset";

        [MenuItem("ShipHdMap/Create HUD Assets")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/ShipHdMap", "Resources");
            if (!File.Exists(Tss)) { File.WriteAllText(Tss, "@import url(\"unity-theme://default\");\n"); AssetDatabase.ImportAsset(Tss); AssetDatabase.Refresh(); }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Tss);
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(Ps);
            if (ps == null) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, Ps); }
            ps.themeStyleSheet = theme; ps.scaleMode = PanelScaleMode.ConstantPixelSize; ps.scale = 1f;
            EditorUtility.SetDirty(ps); AssetDatabase.SaveAssets();
            Debug.Log($"[HudAssets] {Ps} theme={(theme ? theme.name : "null")}");
        }
    }
}
