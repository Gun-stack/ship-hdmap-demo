using UnityEngine;

namespace ShipHdMap
{
    /// First 20 codes of the AprilTag tag36h11 family (public domain code table). Visual only; nothing decodes them.
    public static class AprilTag36h11
    {
        public static readonly ulong[] Codes =
        {
            0xd5d628584UL, 0xd97f18b49UL, 0xdd280910eUL, 0xe479e9c98UL, 0xebcbca822UL,
            0xf31dab3acUL, 0x056a5d085UL, 0x10652e1d4UL, 0x22b1dfeadUL, 0x265ad0472UL,
            0x34fe91b86UL, 0x3ff962cd5UL, 0x43a25329aUL, 0x474b4385fUL, 0x4e9d243e9UL,
            0x5246149aeUL, 0x5997f5538UL, 0x60b8ce8a4UL, 0x6d1c2f7d5UL, 0x7092ea5d3UL,
        };
        public static int Count => Codes.Length;

        /// 8x8 cells: 1-cell black border, 6x6 payload. true = white.
        public static bool[,] Bits(int code)
        {
            ulong v = Codes[code % Codes.Length];
            var b = new bool[8, 8];
            for (int r = 0; r < 6; r++) for (int c = 0; c < 6; c++)
            {
                int bit = 35 - (r * 6 + c);
                b[r + 1, c + 1] = ((v >> bit) & 1UL) == 1UL;
            }
            return b;
        }

        public static Texture2D MakeTexture(int code, int pixelsPerCell = 8)
        {
            var bits = Bits(code); int n = 8 * pixelsPerCell;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                bool white = bits[7 - y / pixelsPerCell, x / pixelsPerCell]; // row 0 at top
                px[y * n + x] = white ? Color.white : Color.black;
            }
            t.SetPixels(px); t.Apply(false, false);
            return t;
        }
    }
}
