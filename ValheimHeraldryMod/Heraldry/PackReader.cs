using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     Turns a charge pack (its files: manifest.json and PNGs) into single charges ready to travel
    ///     (<see cref="ChargeBundle"/>). Pure C#, so the server (no graphics), the game and the preview tool all
    ///     make the very same bytes, and so the same fingerprints.
    ///     <list type="bullet">
    ///         <item>With a manifest: its charges (own PNGs, passed through untouched) and atlases (cells cut out
    ///         and saved as PNGs of their own).</item>
    ///         <item>Without one: every PNG is a charge named after its file ("black_dog.png" → [black dog]),
    ///         with "name.mask.png" as its mask (2026-10-13 design).</item>
    ///     </list>
    ///     Problems skip one charge and are reported through <c>warn</c>.
    /// </summary>
    public static class PackReader
    {
        public const int MaxChargeSide = 1024;
        public const int MaxAtlasSide = 4096;

        public static List<ChargeBundle> Read(Dictionary<string, byte[]> files, string label, Action<string> warn)
        {
            var result = new List<ChargeBundle>();
            if (files.TryGetValue("manifest.json", out var manifestBytes))
            {
                PackManifest m;
                try
                {
                    m = PackManifest.Parse(System.Text.Encoding.UTF8.GetString(manifestBytes));
                }
                catch (FormatException ex)
                {
                    warn($"{label}: manifest.json {ex.Message}; pack skipped.");
                    return result;
                }
                string pack = PackName(string.IsNullOrEmpty(m.pack) ? label : m.pack);
                int listed = (m.charges?.Count ?? 0) + (m.atlases?.Sum(a => a?.charges?.Count ?? 0) ?? 0);
                if (listed == 0) warn($"{pack}: manifest.json lists no charges (check the \"charges\" list and its spelling).");
                foreach (var def in m.charges) Single(files, pack, def, result, warn);
                foreach (var atlas in m.atlases) Atlas(files, pack, atlas, result, warn);
                return result;
            }

            // No manifest: loose PNGs, named after their files
            string loosePack = PackName(label);
            foreach (var kv in files.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                string file = kv.Key;
                if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".mask.png", StringComparison.OrdinalIgnoreCase)) continue;
                string stem = Path.GetFileNameWithoutExtension(file);
                string maskFile = file.Substring(0, file.Length - 4) + ".mask.png";
                var def = new PackManifest.ChargeDef
                {
                    names = new[] { stem.Replace('_', ' ').Trim() },
                    attitudes = new string[0],
                    image = file,
                    mask = files.ContainsKey(maskFile) ? maskFile : null,
                };
                Single(files, loosePack, def, result, warn);
            }
            return result;
        }

        private static void Single(Dictionary<string, byte[]> files, string pack, PackManifest.ChargeDef def, List<ChargeBundle> into, Action<string> warn)
        {
            if (!Valid(def, pack, warn)) return;
            if (string.IsNullOrEmpty(def.image))
            {
                warn($"{pack}: '{def.names[0]}' has no image; skipped.");
                return;
            }
            var image = Png(files, def.image, MaxChargeSide, pack, warn, out int w, out int h);
            if (image == null) return;
            byte[] mask = null;
            if (!string.IsNullOrEmpty(def.mask))
            {
                mask = Png(files, def.mask, MaxChargeSide, pack, warn, out int mw, out int mh);
                if (mask != null && (mw != w || mh != h))
                {
                    warn($"{pack}: mask '{def.mask}' isn't the same size as '{def.image}'; ignored.");
                    mask = null;
                }
            }
            into.Add(ChargeBundle.Create(pack, Clean(def.names), Clean(def.attitudes), def.noLines, image, mask));
        }

        private static void Atlas(Dictionary<string, byte[]> files, string pack, PackManifest.AtlasDef atlas, List<ChargeBundle> into, Action<string> warn)
        {
            if (atlas == null || string.IsNullOrEmpty(atlas.file)) return;
            if (atlas.cellSize < 8 || atlas.cellSize > MaxChargeSide || atlas.cols < 1 || atlas.rows < 1)
            {
                warn($"{pack}: atlas '{atlas.file}' needs cellSize 8-{MaxChargeSide}, cols and rows; skipped.");
                return;
            }
            var bytes = Png(files, atlas.file, MaxAtlasSide, pack, warn, out _, out _);
            if (bytes == null) return;
            Rgba[] px, maskPx = null;
            int w, h;
            try
            {
                px = MiniPng.Decode(bytes, MaxAtlasSide, out w, out h);
            }
            catch (InvalidDataException ex)
            {
                warn($"{pack}: atlas '{atlas.file}': {ex.Message}; skipped.");
                return;
            }
            if (w < atlas.cols * atlas.cellSize || h < atlas.rows * atlas.cellSize)
            {
                warn($"{pack}: '{atlas.file}' is smaller than {atlas.cols}x{atlas.rows} cells of {atlas.cellSize}; skipped.");
                return;
            }
            if (!string.IsNullOrEmpty(atlas.mask))
            {
                var mb = Png(files, atlas.mask, MaxAtlasSide, pack, warn, out _, out _);
                try
                {
                    if (mb != null)
                    {
                        maskPx = MiniPng.Decode(mb, MaxAtlasSide, out int mw, out int mh);
                        if (mw != w || mh != h)
                        {
                            warn($"{pack}: mask '{atlas.mask}' isn't the same size as '{atlas.file}'; ignored.");
                            maskPx = null;
                        }
                    }
                }
                catch (InvalidDataException ex)
                {
                    warn($"{pack}: atlas mask '{atlas.mask}': {ex.Message}; ignored.");
                }
            }
            int cs = atlas.cellSize;
            foreach (var def in atlas.charges)
            {
                if (!Valid(def, pack, warn)) continue;
                if (def.cell < 0 || def.cell >= atlas.cols * atlas.rows)
                {
                    warn($"{pack}: '{def.names[0]}' cell {def.cell} is outside the atlas; skipped.");
                    continue;
                }
                int x0 = def.cell % atlas.cols * cs, y0 = def.cell / atlas.cols * cs;
                var cell = MiniPng.Encode(Cut(px, w, x0, y0, cs), cs, cs);
                var cellMask = maskPx != null ? MiniPng.Encode(Cut(maskPx, w, x0, y0, cs), cs, cs) : null;
                into.Add(ChargeBundle.Create(pack, Clean(def.names), Clean(def.attitudes), def.noLines, cell, cellMask));
            }
        }

        private static bool Valid(PackManifest.ChargeDef def, string pack, Action<string> warn)
        {
            if (def == null) return false;
            if (def.names == null || def.names.Length == 0 || def.names.All(string.IsNullOrWhiteSpace))
            {
                warn($"{pack}: a charge has no names; skipped.");
                return false;
            }
            return true;
        }

        /// <summary>A PNG from the pack, checked (exists, size, header) but not decoded; null on any problem.</summary>
        private static byte[] Png(Dictionary<string, byte[]> files, string file, int maxSide, string pack, Action<string> warn, out int w, out int h)
        {
            w = h = 0;
            string key = file.Replace('\\', '/').TrimStart('/');
            if (key.Contains("../"))
            {
                warn($"{pack}: '{file}' points outside the pack; skipped.");
                return null;
            }
            if (!files.TryGetValue(key, out var bytes))
            {
                warn($"{pack}: '{file}' not found in the pack; skipped.");
                return null;
            }
            if (!MiniPng.Size(bytes, out w, out h))
            {
                warn($"{pack}: '{file}' isn't a PNG; skipped.");
                return null;
            }
            if (w < 1 || h < 1 || w > maxSide || h > maxSide)
            {
                warn($"{pack}: '{file}' is {w}x{h}; the limit is {maxSide} on each side. Skipped.");
                return null;
            }
            return bytes;
        }

        private static Rgba[] Cut(Rgba[] px, int width, int x0, int y0, int size)
        {
            var cell = new Rgba[size * size];
            for (int y = 0; y < size; y++)
                Array.Copy(px, (y0 + y) * width + x0, cell, y * size, size);
            return cell;
        }

        private static string[] Clean(string[] a) =>
            (a ?? new string[0]).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();

        /// <summary>Pack names: lower case, spaces as dashes ("Northern Beasts" → "northern-beasts").</summary>
        public static string PackName(string s) =>
            string.Join("-", (s ?? "").Trim().ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
