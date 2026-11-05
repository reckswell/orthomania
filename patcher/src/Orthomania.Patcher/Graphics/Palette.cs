using osu.Framework.Graphics;
using osuTK.Graphics;

namespace Orthomania.Patcher.Graphics
{
    /// <summary>
    /// osu!(lazer)'s colours: its pink, and the pink colour scheme its overlays use (<c>OverlayColourProvider</c>, hue 333).
    /// </summary>
    public static class Palette
    {
        private const float hue = 333 / 360f;

        private static Color4 scheme(float saturation, float lightness) => Colour4.FromHSL(hue, saturation, lightness);

        public static readonly Color4 PINK = Colour4.FromHex("ff66aa");
        public static readonly Color4 PINK_LIGHT = Colour4.FromHex("ff99cc");
        public static readonly Color4 PINK_DARK = Colour4.FromHex("cc5288");
        public static readonly Color4 PINK_DARKER = Colour4.FromHex("bb1177");
        public static readonly Color4 YELLOW = Colour4.FromHex("ffcc22");
        public static readonly Color4 RED = Colour4.FromHex("ed1121");

        public static readonly Color4 CONTENT1 = scheme(0.4f, 1);
        public static readonly Color4 CONTENT2 = scheme(0.4f, 0.9f);
        public static readonly Color4 LIGHT1 = scheme(0.4f, 0.8f);
        public static readonly Color4 LIGHT3 = scheme(0.4f, 0.7f);
        public static readonly Color4 LIGHT4 = scheme(0.4f, 0.5f);
        public static readonly Color4 FOREGROUND1 = scheme(0.1f, 0.6f);
        public static readonly Color4 BACKGROUND1 = scheme(0.1f, 0.4f);
        public static readonly Color4 BACKGROUND2 = scheme(0.1f, 0.3f);
        public static readonly Color4 BACKGROUND3 = scheme(0.1f, 0.25f);
        public static readonly Color4 BACKGROUND4 = scheme(0.1f, 0.2f);
        public static readonly Color4 BACKGROUND5 = scheme(0.1f, 0.15f);
        public static readonly Color4 BACKGROUND6 = scheme(0.1f, 0.1f);
        public static readonly Color4 DARK4 = scheme(0.2f, 0.2f);
        public static readonly Color4 DARK6 = scheme(0.2f, 0.1f);
    }
}
