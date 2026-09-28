using UnityEngine;

namespace ShipHdMap
{
    /// Every colour the scene paints, in one place. The first block MEANS something and is shared with the web plan
    /// view: web/src/theme/palette.ts carries the same names with the same hex, and palette-sync.test.ts (Vitest)
    /// reads THIS file and fails if a shared name is missing here or differs -- a slot the 3D view fills as
    /// "filled" must be the plan view's "filled" too. Keep each shared line in the exact form
    /// `public const string Name = "#rrggbb";`, which is what that test parses.
    public static class Palette
    {
        // ── shared with web/src/theme/palette.ts ──────────────────────────────
        public const string SlotEmpty = "#199e70";
        public const string SlotFilled = "#3987e5";
        public const string SlotNeedsAdjust = "#d95926";
        public const string SlotUnreachable = "#6b6f78";
        public const string CoverageBlind = "#d03b3b";
        public const string CoverageWeak = "#f0a500";
        public const string CoverageOk = "#8a9aa6";
        public const string Lane = "#e8c547";
        public const string Selection = "#ffb300";
        public const string Seen = "#e040a0";
        public const string SensorCone = "#4cc9f0";

        // ── scene-only (no web counterpart) ────────────────────────────────────
        public const string DeckFloor = "#727b86";
        public const string HullWall = "#34475c";
        public const string Pillar = "#5a636e";
        public const string LashingSocket = "#c3c9cf";
        public const string Pipe = "#c7902e";
        public const string RampPlate = "#626a75";
        public const string SlotOutline = "#d6dde5";
        public const string Quay = "#8e8b85";
        public const string Water = "#1b3a52";
        public const string ParkedCar = "#c9ced6";
        public const string DriveCar = "#e8743b";

        // ── HUD ───────────────────────────────────────────────────────────────
        public const string HudPanel = "#0d1015";
        public const string HudText = "#e6e8eb";
        public const string HudDim = "#8a929e";
        public const string HudAccent = "#4cc9f0";
        public const string HudWarn = "#f0a500";

        /// "#rrggbb" -> Color. Throws on a malformed constant rather than painting it some default: a typo here is a
        /// build-time mistake and should be loud in the EditMode tests, not a quietly grey slot in the browser.
        public static Color C(string hex, float alpha = 1f)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var c)) throw new System.ArgumentException("bad palette colour " + hex);
            c.a = alpha; return c;
        }
    }
}
