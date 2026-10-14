using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jotunn.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     Charge packs: a manifest.json plus PNGs, as a folder or a .zip in BepInEx/config/Heraldry/Charges, or
    ///     received from a server. Each charge is its own PNG or a cell of an atlas; an optional mask on the same
    ///     layout holds interior lines (red), armed (green) and langued (blue). Blazons name them in brackets.
    ///     Two sets: this computer's own packs, and the packs of the server you're connected to. On someone else's
    ///     server only the server's set is used, so everyone sees the same arms (Hippo: "the server asserts
    ///     dominance"); in single player, or when hosting, your own packs are used (and are what you serve).
    ///     Every file is checked before it's decoded, and a bad one only loses that charge.
    /// </summary>
    internal sealed class ChargePacks : IPackCharges
    {
        public const int MaxImageSide = 4096;       // atlas
        public const int MaxChargeSide = 1024;      // one charge or cell
        public const long MaxFileBytes = 16L << 20;

        public static readonly MiniZip.Limits ZipLimits = new MiniZip.Limits { MaxEntries = 512, MaxEntryBytes = MaxFileBytes, MaxTotalBytes = 64L << 20 };

        public static readonly ChargePacks Instance = new ChargePacks();

        /// <summary>Changes whenever the charges available change, so banners know to redraw.</summary>
        public static int Generation { get; private set; }

        public static string Folder => Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "Heraldry"), "Charges");

        /// <summary>One pack ready to send: what a server offers.</summary>
        public sealed class Bundle
        {
            public string Name;
            public int Version;
            public int Charges;
            public string Fingerprint;
            public byte[] Zip;
        }

        private sealed class Drawing
        {
            public string Pack;
            public string Key;
            public string[] Attitudes;
        }

        /// <summary>An index of drawings by name.</summary>
        private sealed class PackSet
        {
            public readonly Dictionary<string, List<Drawing>> ByName = new Dictionary<string, List<Drawing>>();
            public int Count;
        }

        private readonly PackSet local = new PackSet();
        private readonly PackSet server = new PackSet();
        private readonly List<Bundle> localBundles = new List<Bundle>();
        private bool useServer;

        /// <summary>This computer's packs as zips, for serving when this computer is the server.</summary>
        public IReadOnlyList<Bundle> LocalBundles => localBundles;

        public int LocalCount => local.Count;

        /// <summary>Switch to the server's packs (joining someone else's server) or back to your own.</summary>
        public void UseServerPacks(bool on)
        {
            if (useServer == on) return;
            useServer = on;
            if (!on) ClearServer();
            Generation++;
        }

        public void ClearServer()
        {
            server.ByName.Clear();
            server.Count = 0;
            Generation++;
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        ///     Loads every local pack (folders and .zip files) and prepares them for serving. Main thread.
        ///     A dedicated server has no graphics, so it only checks and zips the packs without decoding images.
        /// </summary>
        public void LoadLocal()
        {
            local.ByName.Clear();
            local.Count = 0;
            localBundles.Clear();
            bool decode = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            try
            {
                Directory.CreateDirectory(Folder);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Can't create {Folder}: {ex.Message}");
                return;
            }

            var sources = Directory.GetDirectories(Folder).Concat(Directory.GetFiles(Folder, "*.zip"))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var path in sources)
            {
                string label = Path.GetFileName(path);
                try
                {
                    byte[] zip;
                    Dictionary<string, byte[]> files;
                    if (Directory.Exists(path))
                    {
                        files = ReadFolder(path);
                        zip = MiniZip.Write(files); // deterministic, so an unchanged folder keeps its fingerprint
                    }
                    else
                    {
                        zip = File.ReadAllBytes(path);
                        files = Unzip(zip, label);
                    }
                    if (files == null) continue;
                    var info = LoadInto(local, "local", files, label, decode);
                    if (info == null) continue;
                    info.Zip = zip;
                    info.Fingerprint = MiniZip.Fingerprint(zip);
                    localBundles.Add(info);
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] Charge pack '{label}' failed to load: {ex.Message}");
                }
            }
            Generation++;
            Jotunn.Logger.LogInfo($"[Heraldry] {local.Count} pack charge drawing(s) in {localBundles.Count} local pack(s) from {Folder}");
        }

        /// <summary>A pack received from the server (already checked against its fingerprint). Main thread.</summary>
        public bool LoadServerPack(byte[] zip, string label)
        {
            try
            {
                var files = Unzip(zip, label);
                if (files == null) return false;
                var info = LoadInto(server, "server", files, label, decode: true);
                Generation++;
                return info != null;
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Server charge pack '{label}' failed to load: {ex.Message}");
                return false;
            }
        }

        private static Dictionary<string, byte[]> ReadFolder(string dir)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            string root = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".png" && ext != ".json") continue; // only what a pack uses travels
                var info = new FileInfo(f);
                if (info.Length > MaxFileBytes)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] {Path.GetFileName(dir)}: '{info.Name}' is over {MaxFileBytes >> 20} MB; left out.");
                    continue;
                }
                string rel = Path.GetFullPath(f).Substring(root.Length).Replace('\\', '/');
                files[rel] = File.ReadAllBytes(f);
            }
            return files;
        }

        /// <summary>Unzips a pack; a single top folder ("test-beasts/manifest.json") is stripped.</summary>
        private static Dictionary<string, byte[]> Unzip(byte[] zip, string label)
        {
            Dictionary<string, byte[]> raw;
            try
            {
                raw = MiniZip.Read(zip, ZipLimits);
            }
            catch (InvalidDataException ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] '{label}': {ex.Message}; pack skipped.");
                return null;
            }
            if (raw.ContainsKey("manifest.json")) return raw;
            var manifests = raw.Keys.Where(k => k.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase)).ToList();
            if (manifests.Count != 1)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] '{label}': no manifest.json at the top of the zip; pack skipped.");
                return null;
            }
            string prefix = manifests[0].Substring(0, manifests[0].Length - "manifest.json".Length);
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in raw)
                if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    files[kv.Key.Substring(prefix.Length)] = kv.Value;
            return files;
        }

        /// <summary>Reads the manifest and its images into a set. Returns what it found (for offering), or null.</summary>
        private Bundle LoadInto(PackSet set, string setName, Dictionary<string, byte[]> files, string label, bool decode)
        {
            if (!files.TryGetValue("manifest.json", out var manifestBytes))
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Charge pack '{label}' has no manifest.json; skipped.");
                return null;
            }
            PackManifest m;
            try
            {
                m = PackManifest.Parse(System.Text.Encoding.UTF8.GetString(manifestBytes));
            }
            catch (FormatException ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Charge pack '{label}': manifest.json {ex.Message}; pack skipped.");
                return null;
            }
            string defaultName = Path.GetFileNameWithoutExtension(label);
            string pack = Fold(string.IsNullOrEmpty(m.pack) ? defaultName : m.pack).Replace(' ', '-');
            int listed = (m.charges?.Count ?? 0) + (m.atlases?.Sum(a => a?.charges?.Count ?? 0) ?? 0);
            if (listed == 0)
                Jotunn.Logger.LogWarning($"[Heraldry] {pack}: manifest.json lists no charges (check the \"charges\" list and its spelling).");

            int loaded = 0;
            var cache = new Dictionary<string, Img>(StringComparer.OrdinalIgnoreCase);
            Img Get(string file, int maxSide) =>
                cache.TryGetValue(file, out var c) ? c : cache[file] = LoadImage(files, file, maxSide, pack, decode);

            foreach (var def in m.charges)
            {
                if (!Valid(def, pack)) continue;
                if (string.IsNullOrEmpty(def.image))
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{def.names[0]}' has no image; skipped.");
                    continue;
                }
                var img = Get(def.image, MaxChargeSide);
                if (img == null) continue;
                Rgba[] mask = null;
                if (!string.IsNullOrEmpty(def.mask))
                {
                    var mi = Get(def.mask, MaxChargeSide);
                    if (mi != null && mi.W == img.W && mi.H == img.H) mask = mi.Px;
                    else if (mi != null) Jotunn.Logger.LogWarning($"[Heraldry] {pack}: mask '{def.mask}' isn't the same size as '{def.image}'; ignored.");
                }
                Add(set, setName, pack, def, img, img.Px, mask, ref loaded);
            }

            foreach (var atlas in m.atlases)
            {
                if (atlas == null || string.IsNullOrEmpty(atlas.file)) continue;
                if (atlas.cellSize < 8 || atlas.cellSize > MaxChargeSide || atlas.cols < 1 || atlas.rows < 1)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] {pack}: atlas '{atlas.file}' needs cellSize 8-{MaxChargeSide}, cols and rows; skipped.");
                    continue;
                }
                var img = Get(atlas.file, MaxImageSide);
                if (img == null) continue;
                if (img.W < atlas.cols * atlas.cellSize || img.H < atlas.rows * atlas.cellSize)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{atlas.file}' is smaller than {atlas.cols}x{atlas.rows} cells of {atlas.cellSize}; skipped.");
                    continue;
                }
                Img maskImg = null;
                if (!string.IsNullOrEmpty(atlas.mask))
                {
                    maskImg = Get(atlas.mask, MaxImageSide);
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
                    var cell = new Img { W = cs, H = cs, Px = img.Px != null ? Cut(img, x0, y0, cs) : null };
                    Add(set, setName, pack, def, cell, cell.Px, maskImg?.Px != null ? Cut(maskImg, x0, y0, cs) : null, ref loaded);
                }
            }

            Jotunn.Logger.LogInfo($"[Heraldry] Charge pack '{pack}' (version {m.version}, {setName}): {loaded} drawing(s).");
            return new Bundle { Name = pack, Version = m.version, Charges = loaded };
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

        private void Add(PackSet set, string setName, string pack, PackManifest.ChargeDef def, Img img, Rgba[] px, Rgba[] mask, ref int loaded)
        {
            string key = $"pack:{setName}:{set.Count}";
            if (px != null) ChargeArtLibrary.Register(key, px, img.W, img.H, deriveLines: !def.noLines, mask: mask);
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
                    if (!set.ByName.TryGetValue(k, out var list)) set.ByName[k] = list = new List<Drawing>();
                    if (k == name && list.Count > 0 && list.All(d => d.Pack != pack))
                        Jotunn.Logger.LogWarning($"[Heraldry] [{name}] is in packs '{list[0].Pack}' and '{pack}'; plain [{name}] uses '{list[0].Pack}', write [{pack}:{name}] for the other.");
                    list.Add(drawing);
                }
            }
            set.Count++;
            loaded++;
        }

        // ------------------------------------------------------------------ IPackCharges (for the parser)

        private PackSet Active => useServer ? server : local;

        public string Resolve(string name, IList<string> attitudes, out string unmatched)
        {
            unmatched = null;
            if (!Active.ByName.TryGetValue(name, out var list) || list.Count == 0) return null;
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

        /// <summary>Every charge name in the packs in use, sorted (for basic mode's charge list); "pack:name" forms left out.</summary>
        public List<string> Names()
        {
            var names = Active.ByName.Keys.Where(n => !n.Contains(":")).ToList();
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public string Suggest(string name)
        {
            string best = null;
            int bestD = Math.Max(1, name.Length / 4) + 1;
            foreach (var n in Active.ByName.Keys)
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
            public Rgba[] Px; // rows top to bottom; null when only checked (dedicated server)
            public int W, H;
        }

        /// <summary>Checks the PNG header before decoding; logs and returns null on any problem.</summary>
        private static Img LoadImage(Dictionary<string, byte[]> files, string file, int maxSide, string pack, bool decode)
        {
            string key = file.Replace('\\', '/').TrimStart('/');
            if (key.Contains("../"))
            {
                Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{file}' points outside the pack; skipped.");
                return null;
            }
            if (!files.TryGetValue(key, out var bytes))
            {
                Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{file}' not found in the pack; skipped.");
                return null;
            }
            if (bytes.Length > MaxFileBytes)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] {pack}: '{file}' is over {MaxFileBytes >> 20} MB; skipped.");
                return null;
            }
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
            if (!decode) return new Img { W = w, H = h };

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
        internal static string Fold(string s)
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
