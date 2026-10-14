using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     Lettering for inscriptions, from the game's own Norse font (TextMeshPro asset "Valheim-Norse", which has
    ///     Latin letters and the runic block): the glyphs are added to the font's dynamic atlas, read back from the
    ///     graphics card, and laid out into one distance-field mask per text (TextArtLibrary), so letters stay crisp
    ///     at any size. Main thread, before arms are drawn. Also decides which mottos this player may see
    ///     (the server's ShowMottos; admins always see them; secret codes' own words always show).
    /// </summary>
    internal static class TextArt
    {
        private static TMP_FontAsset font;
        private static bool fontMissingLogged;
        private static readonly HashSet<string> Failed = new HashSet<string>();

        /// <summary>Mottos are hidden from this player (the server's setting, and not an admin).</summary>
        public static bool HideMottos =>
            !HeraldryConfig.ShowMottos && !(ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost());

        public static void EnsureFor(Blazon blazon)
        {
            if (blazon == null) return;
            foreach (var ins in Inscriptions(blazon))
            {
                ins.Hidden = HideMottos && !ins.FromCode;
                if (ins.Hidden) continue;
                string text = ins.Display;
                if (TextArtLibrary.Has(text) || Failed.Contains(text)) continue;
                try
                {
                    if (!Build(text)) Failed.Add(text);
                }
                catch (Exception ex)
                {
                    Failed.Add(text);
                    Jotunn.Logger.LogWarning($"[Heraldry] Lettering '{text}' failed: {ex.Message}");
                }
            }
        }

        private static IEnumerable<Inscription> Inscriptions(Blazon b)
        {
            foreach (var o in b.Ordinaries)
            {
                if (o.Inscription != null) yield return o.Inscription;
                foreach (var c in o.Charges) if (c.Inscription != null) yield return c.Inscription;
            }
            foreach (var c in b.Charges) if (c.Inscription != null) yield return c.Inscription;
        }

        private static TMP_FontAsset Font()
        {
            if (font != null) return font;
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            font = all.FirstOrDefault(f => f.name == "Valheim-Norse")
                   ?? all.FirstOrDefault(f => f.name.IndexOf("Norse", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                              f.name.IndexOf("bold", StringComparison.OrdinalIgnoreCase) < 0);
            if (font == null && !fontMissingLogged)
            {
                fontMissingLogged = true;
                Jotunn.Logger.LogWarning("[Heraldry] The game's Norse font wasn't found; inscriptions show without their words. " +
                                         "Fonts here: " + string.Join(", ", all.Select(f => f.name)));
            }
            else if (font != null)
                Jotunn.Logger.LogInfo($"[Heraldry] Lettering uses the font '{font.name}' ({font.atlasRenderMode}, padding {font.atlasPadding}).");
            return font;
        }


        /// <summary>Lays the text out from the font's glyphs into one mask. False if the font can't do it.</summary>
        private static bool Build(string text)
        {
            var f = Font();
            if (f == null) return false;
            f.TryAddCharacters(text, out string missing);
            if (!string.IsNullOrEmpty(missing))
                Jotunn.Logger.LogWarning($"[Heraldry] The Norse font has no letters for '{missing}' (in '{text}'); they're left out.");

            var face = f.faceInfo;
            int pad = f.atlasPadding;
            float ascender = face.ascentLine, descender = face.descentLine;
            int height = Mathf.CeilToInt(ascender - descender) + pad * 2;

            // First pass: the pen positions, so the mask can be sized
            var placed = new List<(UnityEngine.TextCore.Glyph glyph, float pen)>();
            float x = pad;
            var lookup = f.characterLookupTable;
            for (int i = 0; i < text.Length; i++)
            {
                uint code = char.IsSurrogatePair(text, i) ? (uint)char.ConvertToUtf32(text, i++) : text[i];
                if (!lookup.TryGetValue(code, out var ch) || ch?.glyph == null)
                {
                    x += face.pointSize * 0.3f; // a space, or a letter the font lacks
                    continue;
                }
                placed.Add((ch.glyph, x));
                x += ch.glyph.metrics.horizontalAdvance;
            }
            int width = Mathf.CeilToInt(x) + pad;
            if (placed.Count == 0 || width < 2 || height < 2) return false;

            bool sdf = f.atlasRenderMode.ToString().IndexOf("SDF", StringComparison.OrdinalIgnoreCase) >= 0;
            var mask = new byte[width * height];
            float baseline = pad + ascender; // from the top
            var read = new Dictionary<Texture2D, Color32[]>();
            foreach (var (glyph, pen) in placed)
            {
                var atlasTex = f.atlasTextures != null && glyph.atlasIndex < f.atlasTextures.Length ? f.atlasTextures[glyph.atlasIndex] : f.atlasTexture;
                if (atlasTex == null) continue;
                if (!read.TryGetValue(atlasTex, out var atlas)) read[atlasTex] = atlas = Atlas(atlasTex); // once per atlas per text
                if (atlas == null) continue;
                int aw = atlasTex.width, ah = atlasTex.height;
                var r = glyph.glyphRect; // atlas pixels, origin bottom left
                var m = glyph.metrics;
                // The glyph's box in the mask (with the padding around it, where the distance field fades out)
                int dx0 = Mathf.RoundToInt(pen + m.horizontalBearingX) - pad;
                int dyTop = Mathf.RoundToInt(baseline - m.horizontalBearingY) - pad;
                for (int gy = 0; gy < r.height + pad * 2; gy++)
                {
                    int ay = r.y + r.height - 1 + pad - gy; // atlas rows go up; mask rows go down
                    int my = dyTop + gy;
                    if (ay < 0 || ay >= ah || my < 0 || my >= height) continue;
                    for (int gx = 0; gx < r.width + pad * 2; gx++)
                    {
                        int axp = r.x - pad + gx, mx = dx0 + gx;
                        if (axp < 0 || axp >= aw || mx < 0 || mx >= width) continue;
                        byte v = atlas[ay * aw + axp].a;
                        int k = my * width + mx;
                        if (v > mask[k]) mask[k] = v; // glyph boxes can overlap a little
                    }
                }
            }
            TextArtLibrary.Register(text, mask, width, height, sdf ? pad * 2f : 0f);
            return true;
        }

        /// <summary>
        ///     The atlas's pixels (alpha = the distance field), read fresh for each text. A dynamic atlas is readable,
        ///     and its CPU copy is where new glyphs land first: the copy on the graphics card can lag behind (the
        ///     first in-game test drew only the letters the menus had already used), so read the CPU copy.
        /// </summary>
        private static Color32[] Atlas(Texture2D tex)
        {
            if (tex == null) return null;
            if (tex.isReadable)
            {
                try { return tex.GetPixels32(); }
                catch (UnityException) { /* not readable after all: fall back to the graphics card */ }
            }
            return Shields.Textures.ReadPixels(tex, tex.width, tex.height, linear: true);
        }

        // ------------------------------------------------------------------ mottos hidden from players

        private static readonly Regex Quoted = new Regex("(inscribed\\s*)[\"“”][^\"“”]*[\"“”]?", RegexOptions.IgnoreCase);

        /// <summary>Arms as shown in hover text and tooltips: with the mottos' words left out when they're hidden here.</summary>
        public static string ForDisplay(string visible) =>
            string.IsNullOrEmpty(visible) || !HideMottos ? visible : Quoted.Replace(visible, "$1\"…\"");
    }
}
