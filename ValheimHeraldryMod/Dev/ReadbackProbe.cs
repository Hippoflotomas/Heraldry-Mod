using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Jotunn.Entities;
using TMPro;
using UnityEngine;

namespace ValheimHeraldryMod.Dev
{
    /// <summary>
    ///     Throwaway prototype (build order step 1). Answers two questions before the charge design leans on them:
    ///     1. Can item icons (trophies etc.) be read back as pixels, and at what resolution?
    ///     2. Which fonts does the game ship, and does any of them hold real Unicode runes (U+16A0-U+16FF)?
    ///     Console (F5), in a world:  heraldry_probe [icons|fonts|all] [item-name prefix, default "Trophy"]
    ///     Writes PNGs (icons/previews has 4x/8x stretches and an 8x silhouette) and report.txt to BepInEx/HeraldryProbe.
    /// </summary>
    internal class ReadbackProbe : ConsoleCommand
    {
        public override string Name => "heraldry_probe";
        public override string Help => "Heraldry dev probe: dump item icons and font info to BepInEx/HeraldryProbe. Args: [icons|fonts|all] [item prefix]";

        private const int RunicFirst = 0x16A0;
        private const int RunicLast = 0x16F8;

        public override List<string> CommandOptionList() => new List<string> { "icons", "fonts", "all" };

        public override void Run(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
            string prefix = args.Length > 1 ? args[1] : "Trophy";

            string outDir = Path.Combine(Paths.BepInExRootPath, "HeraldryProbe");
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();
            report.AppendLine($"Heraldry probe, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}, colour space {QualitySettings.activeColorSpace}");
            report.AppendLine();

            try
            {
                if (mode == "icons" || mode == "all")
                    ProbeIcons(prefix, Path.Combine(outDir, "icons"), report);
                if (mode == "fonts" || mode == "all")
                    ProbeFonts(Path.Combine(outDir, "fonts"), report);
            }
            catch (Exception ex)
            {
                report.AppendLine($"PROBE FAILED: {ex}");
            }

            string reportPath = Path.Combine(outDir, "report.txt");
            File.WriteAllText(reportPath, report.ToString());
            global::Console.instance?.Print($"Heraldry probe done: {reportPath}");
            Jotunn.Logger.LogInfo($"Heraldry probe written to {reportPath}");
        }

        // ---------------------------------------------------------------- icons

        private static void ProbeIcons(string prefix, string dir, StringBuilder report)
        {
            report.AppendLine($"== Item icons (prefab names starting with '{prefix}') ==");
            if (ObjectDB.instance == null || ObjectDB.instance.m_items == null || ObjectDB.instance.m_items.Count == 0)
            {
                report.AppendLine("ObjectDB is not loaded. Run this inside a world.");
                report.AppendLine();
                return;
            }
            Directory.CreateDirectory(dir);

            int done = 0, failed = 0;
            foreach (var prefab in ObjectDB.instance.m_items.Where(p => p != null && p.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.name))
            {
                var drop = prefab.GetComponent<ItemDrop>();
                var icons = drop?.m_itemData?.m_shared?.m_icons;
                if (icons == null || icons.Length == 0 || icons[0] == null)
                {
                    report.AppendLine($"{prefab.name}: no icon");
                    continue;
                }

                var sprite = icons[0];
                var tex = sprite.texture;
                var line = new StringBuilder($"{prefab.name}: sprite '{sprite.name}' rect {sprite.rect.width}x{sprite.rect.height}");
                line.Append($", texture '{tex?.name}' {tex?.width}x{tex?.height} {tex?.format} readable={tex?.isReadable}");
                line.Append($", packed={sprite.packed}");
                if (sprite.packed)
                    line.Append($" mode={sprite.packingMode} rotation={sprite.packingRotation}");

                try
                {
                    Rect r = SourceRect(sprite);
                    int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
                    var pixels = ReadRegion(tex, r, w, h, srgb: true);
                    ProbeImage.Save(pixels, w, h, Path.Combine(dir, prefab.name + ".png"));
                    SavePreviews(pixels, w, h, dir, prefab.name);
                    line.Append($" -> saved {w}x{h}; {AlphaStats(pixels, w, h)}");
                    done++;
                }
                catch (Exception ex)
                {
                    line.Append($" -> FAILED: {ex.GetType().Name}: {ex.Message}");
                    failed++;
                }
                report.AppendLine(line.ToString());
            }
            report.AppendLine($"Icons saved: {done}, failed: {failed}");
            report.AppendLine();
        }

        /// <summary>
        ///     How the icon would look as a charge: proper colour stretched 4x and 8x (smooth, like the GPU would),
        ///     and a silhouette at 8x made by thresholding the smoothly stretched alpha. That threshold is a close
        ///     stand-in for what the SDF route gives, so it shows how much outline detail survives.
        ///     Previews sit on a mid-grey "field" so soft edges are visible.
        /// </summary>
        private static void SavePreviews(Color32[] px, int w, int h, string dir, string name)
        {
            string previews = Path.Combine(dir, "previews");
            Directory.CreateDirectory(previews);
            var field = new Color32(128, 128, 128, 255);
            foreach (int scale in new[] { 4, 8 })
            {
                int sw = w * scale, sh = h * scale;
                var outPx = new Color32[sw * sh];
                for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    var c = Bilinear(px, w, h, (x + 0.5f) / scale - 0.5f, (y + 0.5f) / scale - 0.5f);
                    float a = c.a / 255f;
                    outPx[y * sw + x] = Color32.Lerp(field, new Color32(c.r, c.g, c.b, 255), a);
                }
                ProbeImage.Save(outPx, sw, sh, Path.Combine(previews, $"{name}_proper_x{scale}.png"));
            }

            {
                const int scale = 8;
                int sw = w * scale, sh = h * scale;
                var outPx = new Color32[sw * sh];
                var ink = new Color32(20, 20, 20, 255);       // sable
                var ground = new Color32(235, 225, 200, 255); // argent-ish cloth
                for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                {
                    float a = Bilinear(px, w, h, (x + 0.5f) / scale - 0.5f, (y + 0.5f) / scale - 0.5f).a / 255f;
                    // edge at alpha 0.5, anti-aliased over about one output pixel
                    float t = Mathf.Clamp01((a - 0.5f) * scale * 0.5f + 0.5f);
                    outPx[y * sw + x] = Color32.Lerp(ground, ink, t);
                }
                ProbeImage.Save(outPx, sw, sh, Path.Combine(previews, $"{name}_silhouette_x{scale}.png"));
            }
        }

        /// <summary>Bilinear sample in source pixel coordinates (pixel centres at integers), clamped at the edges.</summary>
        private static Color32 Bilinear(Color32[] px, int w, int h, float fx, float fy)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, w - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, h - 1);
            int x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
            float tx = Mathf.Clamp01(fx - x0), ty = Mathf.Clamp01(fy - y0);
            var top = Color32.Lerp(px[y0 * w + x0], px[y0 * w + x1], tx);
            var bottom = Color32.Lerp(px[y1 * w + x0], px[y1 * w + x1], tx);
            return Color32.Lerp(top, bottom, ty);
        }

        /// <summary>Pixel rectangle of the sprite inside its texture. Tight-packed sprites have no valid textureRect.</summary>
        private static Rect SourceRect(Sprite sprite)
        {
            if (sprite.packed && sprite.packingMode == SpritePackingMode.Tight)
                throw new NotSupportedException("tight-packed sprite: textureRect is not available");
            return sprite.packed ? sprite.textureRect : sprite.rect;
        }

        /// <summary>
        ///     GPU copy of a region of any texture (compressed and non-readable included) into a pixel array.
        ///     Same idea as ShieldShare's ReadTexturePixels, plus a source rectangle and an sRGB target for colour images.
        /// </summary>
        internal static Color32[] ReadRegion(Texture source, Rect region, int width, int height, bool srgb)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                var scale = new Vector2(region.width / source.width, region.height / source.height);
                var offset = new Vector2(region.x / source.width, region.y / source.height);
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(0, 0, 0, 0));
                Graphics.Blit(source, rt, scale, offset);
                RenderTexture.active = rt;
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false, !srgb);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply();
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null)
                    UnityEngine.Object.Destroy(copy);
            }
        }

        /// <summary>How much of the image is opaque, and whether the shape touches the edge (matters for SDF margins).</summary>
        private static string AlphaStats(Color32[] px, int w, int h)
        {
            int opaque = 0, partial = 0;
            bool touchesEdge = false;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte a = px[y * w + x].a;
                if (a >= 250) opaque++;
                else if (a > 5) partial++;
                if (a > 5 && (x == 0 || y == 0 || x == w - 1 || y == h - 1))
                    touchesEdge = true;
            }
            float n = w * h;
            return $"opaque {opaque / n:P0}, soft edge {partial / n:P0}, touches border={touchesEdge}";
        }

        // ---------------------------------------------------------------- fonts

        private static void ProbeFonts(string dir, StringBuilder report)
        {
            Directory.CreateDirectory(dir);

            report.AppendLine("== TextMeshPro font assets ==");
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>().OrderBy(f => f.name))
            {
                var table = font.characterLookupTable;
                int runic = table == null ? 0 : table.Keys.Count(k => k >= RunicFirst && k <= RunicLast);
                int latin = table == null ? 0 : table.Keys.Count(k => (k >= 'A' && k <= 'Z') || (k >= 'a' && k <= 'z'));
                var atlas = font.atlasTexture;
                report.AppendLine($"{font.name}: characters {table?.Count ?? 0} (runic {runic}, latin letters {latin}), " +
                                  $"atlas {(atlas == null ? "none" : $"{atlas.width}x{atlas.height} {atlas.format}")}, " +
                                  $"fallbacks [{string.Join(", ", (font.fallbackFontAssetTable ?? new List<TMP_FontAsset>()).Where(f => f != null).Select(f => f.name))}]");
                if (runic > 0)
                    report.AppendLine($"    runic code points: {string.Join(" ", table.Keys.Where(k => k >= RunicFirst && k <= RunicLast).OrderBy(k => k).Select(k => $"U+{k:X4}"))}");

                if (atlas != null)
                {
                    try
                    {
                        var px = ReadRegion(atlas, new Rect(0, 0, atlas.width, atlas.height), atlas.width, atlas.height, srgb: false);
                        if (atlas.format == TextureFormat.Alpha8)
                            for (int i = 0; i < px.Length; i++)
                                px[i] = new Color32(px[i].a, px[i].a, px[i].a, 255);   // make the SDF visible in an image viewer
                        ProbeImage.Save(px, atlas.width, atlas.height, Path.Combine(dir, Safe(font.name) + "_atlas.png"));
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"    atlas dump FAILED: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            report.AppendLine();

            report.AppendLine("== Legacy Unity fonts ==");
            foreach (var font in Resources.FindObjectsOfTypeAll<Font>().OrderBy(f => f.name))
                report.AppendLine($"{font.name}: dynamic={font.dynamic}, has U+16A0 (Fehu)={font.HasCharacter('ᚠ')}, has 'A'={font.HasCharacter('A')}");
            report.AppendLine();

            // Where each font is actually used. The rune text shown when reading a runestone should turn up here.
            report.AppendLine("== TextMeshPro text objects by font (first 5 paths each) ==");
            foreach (var group in Resources.FindObjectsOfTypeAll<TMP_Text>()
                         .Where(t => t != null && t.font != null)
                         .GroupBy(t => t.font.name)
                         .OrderBy(g => g.Key))
            {
                report.AppendLine($"{group.Key}: {group.Count()} objects");
                foreach (var t in group.Take(5))
                {
                    string sample = (t.text ?? "").Replace("\n", " ");
                    if (sample.Length > 40) sample = sample.Substring(0, 40) + "...";
                    report.AppendLine($"    {PathOf(t.transform)}  \"{sample}\"");
                }
            }
            report.AppendLine();
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent)
                parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string Safe(string name) =>
            string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
