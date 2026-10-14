using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     The game logos the secret codes use ([logo:irongate], [logo:valheim]), read from the player's own game
    ///     at runtime (nothing redistributed). They live in the start scene, so they're captured at the main menu
    ///     and kept (as charge art) for the session; if they can't be found the charge shows as the placeholder.
    ///     Iron Gate: the gate only (Hippo, 2026-10-14), white on black in the file, so its brightness is the shape.
    ///     Valheim: the fiery main-menu logo, in its own colours.
    /// </summary>
    [HarmonyPatch]
    internal static class LogoArt
    {
        private sealed class Logo
        {
            public string Key, Sprite;
            public bool BrightnessIsShape, GateOnly;
        }

        private static readonly Logo[] Logos =
        {
            new Logo { Key = "logo:irongate", Sprite = "irongatelogo_smaller", BrightnessIsShape = true, GateOnly = true },
            new Logo { Key = "logo:valheim", Sprite = "Logo2_menu_highres" },
        };

        private static readonly HashSet<string> Logged = new HashSet<string>();

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        [HarmonyPostfix]
        private static void AtMainMenu() => Capture();

        /// <summary>Main thread, before arms are drawn: a logo not captured yet gets one more try.</summary>
        public static void EnsureFor(Blazon b)
        {
            if (b == null) return;
            foreach (var c in b.Charges)
                if (c.ArtKey != null && c.ArtKey.StartsWith("logo:", StringComparison.Ordinal) && !ChargeArtLibrary.Has(c.ArtKey))
                {
                    Capture();
                    return;
                }
        }

        private static void Capture()
        {
            Sprite[] sprites = null;
            foreach (var logo in Logos)
            {
                if (ChargeArtLibrary.Has(logo.Key)) continue;
                try
                {
                    sprites = sprites ?? Resources.FindObjectsOfTypeAll<Sprite>();
                    var sprite = sprites.FirstOrDefault(s => s != null && s.name == logo.Sprite);
                    Texture2D tex = sprite != null ? sprite.texture : Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(t => t.name == logo.Sprite);
                    if (tex == null)
                    {
                        if (Logged.Add(logo.Key)) Jotunn.Logger.LogWarning($"[Heraldry] Logo '{logo.Sprite}' isn't loaded; [{logo.Key}] shows as the placeholder for now.");
                        continue;
                    }
                    var rect = sprite != null ? sprite.textureRect : new Rect(0, 0, tex.width, tex.height);
                    int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
                    var all = Shields.Textures.ReadPixels(tex, tex.width, tex.height, linear: false); // bottom row first
                    var px = new Rgba[w * h];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            var c = all[((int)rect.y + h - 1 - y) * tex.width + (int)rect.x + x];
                            px[y * w + x] = logo.BrightnessIsShape
                                ? new Rgba(255, 255, 255, (byte)((c.r * 299 + c.g * 587 + c.b * 114) / 1000))
                                : new Rgba(c.r, c.g, c.b, c.a);
                        }
                    if (logo.GateOnly) px = TopCluster(px, ref w, ref h);
                    ChargeArtLibrary.Register(logo.Key, px, w, h, deriveLines: false);
                    Jotunn.Logger.LogInfo($"[Heraldry] Logo '{logo.Sprite}' captured for [{logo.Key}] ({w}x{h}).");
                }
                catch (Exception ex)
                {
                    if (Logged.Add(logo.Key)) Jotunn.Logger.LogWarning($"[Heraldry] Logo '{logo.Sprite}' couldn't be read: {ex.Message}");
                }
            }
        }

        /// <summary>
        ///     The top part of a stacked logo (the gate above "IRON GATE STUDIO"): rows with something in them, taken
        ///     from the top until the first clear gap of 1.2% of the height, cropped to what's there.
        /// </summary>
        private static Rgba[] TopCluster(Rgba[] px, ref int w, ref int h)
        {
            int gapNeeded = Math.Max(3, h * 12 / 1000);
            int first = -1, last = -1, gap = 0;
            for (int y = 0; y < h; y++)
            {
                bool any = false;
                for (int x = 0; x < w && !any; x++) any = px[y * w + x].A > 128;
                if (any)
                {
                    if (first < 0) first = y;
                    last = y;
                    gap = 0;
                }
                else if (first >= 0 && ++gap >= gapNeeded) break;
            }
            if (first < 0) return px;
            int left = w, right = -1;
            for (int y = first; y <= last; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].A > 128) { left = Math.Min(left, x); right = Math.Max(right, x); }
            int pad = 4;
            int x0 = Math.Max(0, left - pad), x1 = Math.Min(w - 1, right + pad), y0 = Math.Max(0, first - pad), y1 = Math.Min(h - 1, last + pad);
            int nw = x1 - x0 + 1, nh = y1 - y0 + 1;
            var crop = new Rgba[nw * nh];
            for (int y = 0; y < nh; y++)
                Array.Copy(px, (y0 + y) * w + x0, crop, y * nw, nw);
            w = nw;
            h = nh;
            return crop;
        }
    }
}
