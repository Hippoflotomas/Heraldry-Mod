using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     Step-2 stand-in for the real blazon renderer (build order step 3). It only proves the plumbing:
    ///     the first word picks a tincture and fills the whole field. Anything it doesn't understand draws the
    ///     magenta-and-black "missing" quarters, so a broken pipeline is obvious at a glance.
    ///     Pure function of (text, width, height): every client gets identical pixels.
    /// </summary>
    internal static class PlaceholderRenderer
    {
        /// <summary>Undyed linen, for a banner nobody has written on yet.</summary>
        private static readonly Color32 Linen = new Color32(214, 204, 178, 255);

        // Placeholder dyed-cloth values, not final. The palette is its own design pass later.
        private static readonly Dictionary<string, Color32> Tinctures = new Dictionary<string, Color32>
        {
            { "or", new Color32(201, 160, 52, 255) },
            { "argent", new Color32(232, 228, 216, 255) },
            { "azure", new Color32(38, 72, 138, 255) },
            { "gules", new Color32(160, 32, 36, 255) },
            { "sable", new Color32(24, 22, 22, 255) },
            { "vert", new Color32(36, 106, 54, 255) },
            { "purpure", new Color32(102, 44, 110, 255) },
        };

        private static readonly Color32 Magenta = new Color32(255, 0, 255, 255);
        private static readonly Color32 Black = new Color32(0, 0, 0, 255);

        /// <summary>Rows bottom-to-top, as Texture2D.SetPixels32 expects.</summary>
        public static Color32[] Render(string normalisedText, int width, int height)
        {
            var px = new Color32[width * height];

            if (string.IsNullOrEmpty(normalisedText))
            {
                Fill(px, Linen);
                return px;
            }

            string first = normalisedText.Split(' ')[0].TrimEnd(',', '.', ';');
            if (TryTincture(first, out var colour))
            {
                Fill(px, colour);
                return px;
            }

            // Not understood: quartered magenta and black
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                px[y * width + x] = (x < width / 2) == (y >= height / 2) ? Magenta : Black;
            return px;
        }

        private static bool TryTincture(string word, out Color32 colour)
        {
            if (Tinctures.TryGetValue(word, out colour))
                return true;

            // "#rrggbb" (the hex toggle is a later admin setting; the placeholder always accepts it)
            if (word.Length == 7 && word[0] == '#' &&
                int.TryParse(word.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                colour = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
                return true;
            }
            return false;
        }

        private static void Fill(Color32[] px, Color32 c)
        {
            for (int i = 0; i < px.Length; i++)
                px[i] = c;
        }
    }
}
