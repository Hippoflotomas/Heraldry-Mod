using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Jotunn.Utils;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     Charge packs on this computer: BepInEx/config/Heraldry/Charges/&lt;pack folder&gt;/manifest.json plus images.
    ///     Each charge is its own PNG, or a cell of an atlas PNG; an optional mask image on the same layout holds
    ///     interior lines (red), armed (green) and langued (blue). Blazons name them in brackets: "[wolf]".
    ///     Every image is checked before it's decoded, and a bad one only loses that charge.
    /// </summary>
    internal sealed class ChargePacks : IPackCharges
    {
        // Limits (local packs; step 8 makes the server's versions of these admin settings)
        private const int MaxImageSide = 4096;      // atlas
        private const int MaxChargeSide = 1024;     // one charge or cell
        private const long MaxFileBytes = 16L << 20;

        private sealed class Drawing
        {
            public string Pack;
            public string Key;
            public string[] Attitudes;
        }

        public static readonly ChargePacks Instance = new ChargePacks();

        private readonly Dictionary<string, List<Drawing>> byName = new Dictionary<string, List<Drawing>>();

        public static string Folder => Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "Heraldry"), "Charges");

        public int Count { get; private set; }

        /// <summary>Loads every pack folder. Main thread (images are decoded with Unity).</summary>
        public void LoadAll()
        {
            byName.Clear();
            Count = 0;
            try
            {
                Directory.CreateDirectory(Folder);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Can't create {Folder}: {ex.Message}");
                return;
            }
            foreach (var dir in Directory.GetDirectories(Folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    LoadPack(dir);
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] Charge pack '{Path.GetFileName(dir)}' failed to load: {ex.Message}");
                }
            }
            foreach (var zip in Directory.GetFiles(Folder, "*.zip"))
                Jotunn.Logger.LogWarning($"[Heraldry] '{Path.GetFileName(zip)}': zipped packs aren't read yet; unzip it into its own folder.");
            Jotunn.Logger.LogInfo($"[Heraldry] {Count} pack charge drawing(s) loaded from {Folder}");
        }

        private void LoadPack(string dir)
        {
            string manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Charge pack folder '{Path.GetFileName(dir)}' has no manifest.json; skipped.");
                return;
            }
            PackManifest m;
            try
            {
                m = PackManifest.Parse(File.ReadAllText(manifestPath));
            }
            catch (FormatException ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Charge pack '{Path.GetFileName(dir)}': manifest.json {ex.Message}; pack skipped.");
                return;
            }
            string pack = Fold(string.IsNullOrEmpty(m.pack) ? Path.GetFileName(dir) : m.pack).Replace(' ', '-');
            int loaded = 0;
            int listed = (m.charges?.Count ?? 0) + (m.atlases?.Sum(a => a?.charges?.Count ?? 0) ?? 0);
            if (listed == 0)
                Jotunn.Logger.LogWarning($"[Heraldry] {pack}: manifest.json lists no charges (check the \"charges\" list and its spelling).");

            if (m.charges != null)
                foreach (var def in m.charges)
                {
                    if (!Valid(def, pack)) continue;
                    if (string.IsNullOrEmpty(def.image))
                    {
                        Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{def.names[0]}' has no image; skipped.");
                        continue;
                    }
                    var img = LoadImage(dir, def.image, MaxChargeSide, pack);
                    if (img == null) continue;
                    Rgba[] mask = null;
                    if (!string.IsNullOrEmpty(def.mask))
                    {
                        var mi = LoadImage(dir, def.mask, MaxChargeSide, pack);
                        if (mi != null && mi.W == img.W && mi.H == img.H) mask = mi.Px;
                        else if (mi != null) Jotunn.Logger.LogWarning($"[Heraldry] {pack}: mask '{def.mask}' isn't the same size as '{def.image}'; ignored.");
                    }
                    Add(pack, def, img.Px, img.W, img.H, mask, ref loaded);
                }

            if (m.atlases != null)
                foreach (var atlas in m.atlases)
                {
                    if (atlas == null || string.IsNullOrEmpty(atlas.file) || atlas.charges == null) continue;
                    if (atlas.cellSize < 8 || atlas.cellSize > MaxChargeSide || atlas.cols < 1 || atlas.rows < 1)
                    {
                        Jotunn.Logger.LogWarning($"[Heraldry] {pack}: atlas '{atlas.file}' needs cellSize 8-{MaxChargeSide}, cols and rows; skipped.");
                        continue;
                    }
                    var img = LoadImage(dir, atlas.file, MaxImageSide, pack);
                    if (img == null) continue;
                    if (img.W < atlas.cols * atlas.cellSize || img.H < atlas.rows * atlas.cellSize)
                    {
                        Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{atlas.file}' is smaller than {atlas.cols}x{atlas.rows} cells of {atlas.cellSize}; skipped.");
                        continue;
                    }
                    Img maskImg = null;
                    if (!string.IsNullOrEmpty(atlas.mask))
                    {
                        maskImg = LoadImage(dir, atlas.mask, MaxImageSide, pack);
                        if (maskImg != null && (maskImg.W != img.W || maskImg.H != img.H))
                        {
                            Jotunn.Logger.LogWarning($"[Heraldry] {pack}: mask '{atlas.mask}' isn't the same size as '{atlas.file}'; ignored.");
                            maskImg = null;
                        }
                    }
                    foreach (var def in atlas.charges)
                    {
                        if (!Valid(def, pack)) continue;
                        if (def.cell < 0 || def.cell >= atlas.cols * atlas.rows)
                        {
                            Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{def.names[0]}' cell {def.cell} is outside the atlas; skipped.");
                            continue;
                        }
                        int cs = atlas.cellSize, x0 = def.cell % atlas.cols * cs, y0 = def.cell / atlas.cols * cs;
                        Add(pack, def, Cut(img, x0, y0, cs), cs, cs, maskImg != null ? Cut(maskImg, x0, y0, cs) : null, ref loaded);
                    }
                }

            Jotunn.Logger.LogInfo($"[Heraldry] Charge pack '{pack}' (version {m.version}): {loaded} drawing(s).");
        }

        private static bool Valid(PackManifest.ChargeDef def, string pack)
        {
            if (def == null) return false;
            if (def.names == null || def.names.Length == 0 || def.names.All(string.IsNullOrWhiteSpace))
            {
                Jotunn.Logger.LogWarning($"[Heraldry] {pack}: a charge has no names; skipped.");
                return false;
            }
            return true;
        }

        private void Add(string pack, PackManifest.ChargeDef def, Rgba[] px, int w, int h, Rgba[] mask, ref int loaded)
        {
            string key = $"pack:{pack}:{Count}";
            ChargeArtLibrary.Register(key, px, w, h, deriveLines: !def.noLines, mask: mask);
            var drawing = new Drawing
            {
                Pack = pack,
                Key = key,
                Attitudes = (def.attitudes ?? new string[0]).Where(a => !string.IsNullOrWhiteSpace(a)).Select(Fold).ToArray(),
            };
            foreach (var raw in def.names)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string name = Fold(raw);
                foreach (var k in new[] { name, pack + ":" + name })
                {
                    if (!byName.TryGetValue(k, out var list)) byName[k] = list = new List<Drawing>();
                    if (k == name && list.Count > 0 && list[0].Pack != pack && list.All(d => d.Pack != pack))
                        Jotunn.Logger.LogWarning($"[Heraldry] [{name}] is in packs '{list[0].Pack}' and '{pack}'; plain [{name}] uses '{list[0].Pack}', write [{pack}:{name}] for the other.");
                    list.Add(drawing);
                }
            }
            Count++;
            loaded++;
        }

        // ------------------------------------------------------------------ IPackCharges (for the parser)

        public string Resolve(string name, IList<string> attitudes, out string unmatched)
        {
            unmatched = null;
            if (!byName.TryGetValue(name, out var list) || list.Count == 0) return null;
            // A plain name only uses the first pack that has it
            string pack = list[0].Pack;
            var wanted = attitudes.Where(a => !Lexicon.IsFlip(a)).ToList();
            Drawing best = null;
            int bestScore = int.MinValue;
            foreach (var d in list)
            {
                if (d.Pack != pack && !name.Contains(":")) continue;
                int matches = wanted.Count(a => d.Attitudes.Contains(a));
                int score = matches * 100 - d.Attitudes.Length; // most matches, then the plainest drawing
                if (score > bestScore) { bestScore = score; best = d; }
            }
            if (best == null) return null;
            unmatched = wanted.FirstOrDefault(a => !best.Attitudes.Contains(a) && Lexicon.Attitudes.Contains(a));
            return best.Key;
        }

        public string Suggest(string name)
        {
            string best = null;
            int bestD = Math.Max(1, name.Length / 4) + 1;
            foreach (var n in byName.Keys)
            {
                if (n.Contains(":") || Math.Abs(n.Length - name.Length) >= bestD) continue;
                int d = Levenshtein(name, n);
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }

        // ------------------------------------------------------------------ images

        private sealed class Img
        {
            public Rgba[] Px; // rows top to bottom
            public int W, H;
        }

        /// <summary>Checks the file and its PNG header before decoding; logs and returns null on any problem.</summary>
        private static Img LoadImage(string dir, string file, int maxSide, string pack)
        {
            string path = Path.GetFullPath(Path.Combine(dir, file));
            if (!path.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
            {
                Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{file}' points outside the pack folder; skipped.");
                return null;
            }
            if (!File.Exists(path))
            {
                Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{file}' not found; skipped.");
                return null;
            }
            var info = new FileInfo(path);
            if (info.Length > MaxFileBytes)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{file}' is over {MaxFileBytes >> 20} MB; skipped.");
                return null;
            }
            byte[] bytes = File.ReadAllBytes(path);
            if (!PngSize(bytes, out int w, out int h))
            {
                Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{file}' isn't a PNG; skipped.");
                return null;
            }
            if (w < 1 || h < 1 || w > maxSide || h > maxSide)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{file}' is {w}x{h}; the limit is {maxSide} on each side. Skipped.");
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!AssetUtils.LoadImage(tex, bytes) || tex.width != w || tex.height != h)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{file}' couldn't be decoded; skipped.");
                    return null;
                }
                var raw = tex.GetPixels32(); // bottom row first
                var px = new Rgba[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = raw[(h - 1 - y) * w + x];
                    px[y * w + x] = new Rgba(c.r, c.g, c.b, c.a);
                }
                return new Img { Px = px, W = w, H = h };
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        /// <summary>Width and height from the PNG signature and IHDR chunk, without decoding.</summary>
        private static bool PngSize(byte[] b, out int w, out int h)
        {
            w = h = 0;
            byte[] sig = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (b.Length < 24) return false;
            for (int i = 0; i < 8; i++) if (b[i] != sig[i]) return false;
            if (b[12] != 'I' || b[13] != 'H' || b[14] != 'D' || b[15] != 'R') return false;
            w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return true;
        }

        private static Rgba[] Cut(Img img, int x0, int y0, int size)
        {
            var px = new Rgba[size * size];
            for (int y = 0; y < size; y++)
                Array.Copy(img.Px, (y0 + y) * img.W + x0, px, y * size, size);
            return px;
        }

        /// <summary>Same folding as the parser: lower case, accents off, single spaces.</summary>
        private static string Fold(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD))
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return string.Join(" ", sb.ToString().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static int Levenshtein(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
