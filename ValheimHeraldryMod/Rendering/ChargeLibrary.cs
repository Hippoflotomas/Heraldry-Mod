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
    ///     Every picture charge this computer knows (2026-10-14 rework; replaces the per-pack ChargePacks):
    ///     <list type="bullet">
    ///         <item>your own, from <c>Documents\ValheimHeraldryMod\Charges</c> (pack folders, .zip packs, or loose PNGs);</item>
    ///         <item>downloaded ones, <c>...\Downloaded\&lt;fingerprint&gt;.hcharge</c>, approved one by one;</item>
    ///         <item>when this computer is the server (hosting, single player, dedicated): its server folder,
    ///         <c>...\ServerCharges</c> (configurable);</item>
    ///         <item>on someone else's server: the server's list of its charges (names and fingerprints), so they
    ///         can be written before their pictures are here.</item>
    ///     </list>
    ///     Each charge is identified by its fingerprint (<see cref="ChargeBundle"/>); blazons carry fingerprints in
    ///     their hidden tail (<see cref="BlazonText"/>). Pictures are decoded only when something shows them.
    ///     Blocked (personal) and blacklisted (server) charges draw as the placeholder roundel.
    /// </summary>
    internal sealed class ChargeLibrary : IPackCharges
    {
        public static readonly ChargeLibrary Instance = new ChargeLibrary();

        /// <summary>Changes whenever which pictures can be drawn changes, so arms with [charges] redraw.</summary>
        public static int Generation { get; private set; }

        public readonly ChargeIndex Index = new ChargeIndex();

        private readonly Dictionary<string, ChargeBundle> bundles = new Dictionary<string, ChargeBundle>();
        private readonly HashSet<string> serverFolder = new HashSet<string>();  // what this computer serves as a server
        private readonly HashSet<string> downloaded = new HashSet<string>();
        private readonly HashSet<string> builtIn = new HashSet<string>();
        private readonly HashSet<string> decoded = new HashSet<string>();
        private readonly Dictionary<string, string> blocked = new Dictionary<string, string>();   // fingerprint -> name
        private readonly Dictionary<string, string> blacklist = new Dictionary<string, string>(); // fingerprint -> name

        public sealed class Waiting
        {
            public string Fingerprint;
            public string Name;
            public bool FromServer;  // on the server's own list (else another player's)
            public long Size;        // 0 = not known
            public string Status = "waiting for heraldry_get";
        }

        private readonly Dictionary<string, Waiting> pending = new Dictionary<string, Waiting>();
        public IEnumerable<Waiting> Pending => pending.Values;

        private static bool HasGraphics => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        // ================================================================== folders

        /// <summary>Documents\ValheimHeraldryMod, or the folder set in the config.</summary>
        public static string Root
        {
            get
            {
                string custom = HeraldryConfig.ChargeRoot;
                if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs)) docs = BepInEx.Paths.ConfigPath; // a server account without Documents
                return Path.Combine(docs, "ValheimHeraldryMod");
            }
        }

        public static string OwnFolder => Path.Combine(Root, "Charges");
        public static string DownloadFolder => Path.Combine(Root, "Downloaded");
        private static string BlockFile => Path.Combine(Root, "blocked.txt");

        public static string ServerFolder
        {
            get
            {
                string custom = HeraldryConfig.ServerChargeFolder;
                return string.IsNullOrWhiteSpace(custom) ? Path.Combine(Root, "ServerCharges") : custom.Trim();
            }
        }

        public static string BlacklistFile => Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "ValheimHeraldryMod"), "blacklist.txt");

        // ================================================================== loading

        /// <summary>Your own charges and your downloads (again, for heraldry_reload). Main thread.</summary>
        public void LoadOwn()
        {
            LoadBuiltIn();
            foreach (var fp in bundles.Keys.Where(k => !serverFolder.Contains(k) && !builtIn.Contains(k)).ToList()) Forget(fp);
            Index.RemoveSource(ChargeSource.Own);
            Index.RemoveSource(ChargeSource.Downloaded);
            downloaded.Clear();
            Index.OwnFirst = HeraldryConfig.OwnChargesFirst;

            int own = 0;
            foreach (var b in ReadPacks(OwnFolder, "your charges"))
            {
                Keep(b, ChargeSource.Own);
                own++;
            }

            int got = 0;
            try
            {
                Directory.CreateDirectory(DownloadFolder);
                foreach (var file in Directory.GetFiles(DownloadFolder, "*.hcharge"))
                {
                    try
                    {
                        var b = ChargeBundle.Read(File.ReadAllBytes(file), HeraldryConfig.MaxChargeBytes);
                        if (Path.GetFileNameWithoutExtension(file) != b.Fingerprint) continue; // damaged: fetched again when needed
                        Keep(b, ChargeSource.Downloaded);
                        downloaded.Add(b.Fingerprint);
                        got++;
                    }
                    catch (Exception ex)
                    {
                        Jotunn.Logger.LogWarning($"[Heraldry] Downloaded charge '{Path.GetFileName(file)}' unreadable ({ex.Message}); ignored.");
                    }
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Can't read {DownloadFolder}: {ex.Message}");
            }

            LoadBlocked();
            Generation++;
            Jotunn.Logger.LogInfo($"[Heraldry] {own} charge(s) of your own in {OwnFolder}; {got} downloaded in {DownloadFolder}.");
        }

        /// <summary>Charges built into the mod, which every player has: [hippoflotomas] (Hippo's own art).</summary>
        private void LoadBuiltIn()
        {
            if (builtIn.Count > 0) return;
            foreach (var (resource, name) in new[] { ("ValheimHeraldryMod.Hippoflotomas.png", "hippoflotomas") })
            {
                try
                {
                    using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                    {
                        if (stream == null) { Jotunn.Logger.LogWarning($"[Heraldry] Built-in charge {resource} is missing."); continue; }
                        var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        var b = ChargeBundle.Create("built-in", new[] { name }, new string[0], noLines: true, image: ms.ToArray(), mask: null);
                        bundles[b.Fingerprint] = b;
                        Index.Add(b.Fingerprint, b.Pack, b.Names, b.Attitudes, ChargeSource.BuiltIn, b.Bytes.Length);
                        builtIn.Add(b.Fingerprint);
                    }
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] Built-in charge {resource} failed: {ex.Message}");
                }
            }
        }

        /// <summary>The server folder: what this computer serves while it's the server. Main thread.</summary>
        public void LoadServerFolder()
        {
            foreach (var fp in serverFolder.ToList()) { serverFolder.Remove(fp); if (!downloaded.Contains(fp) && Index.Get(fp)?.Source != ChargeSource.Own) Forget(fp); }
            Index.RemoveSource(ChargeSource.Server);
            int n = 0;
            foreach (var b in ReadPacks(ServerFolder, "server charges"))
            {
                Keep(b, ChargeSource.Server);
                serverFolder.Add(b.Fingerprint);
                n++;
            }
            LoadBlacklistFile();
            Generation++;
            Jotunn.Logger.LogInfo($"[Heraldry] {n} server charge(s) in {ServerFolder}.");
        }

        /// <summary>Left a game: forget the server's list and folder; your own charges stay.</summary>
        public void LeaveServer()
        {
            foreach (var fp in serverFolder.ToList()) { serverFolder.Remove(fp); if (!downloaded.Contains(fp) && Index.Get(fp)?.Source != ChargeSource.Own) Forget(fp); }
            Index.RemoveSource(ChargeSource.Server);
            pending.Clear();
            blacklist.Clear();
            Changed();
            LoadOwn(); // puts back own entries a server entry of the same fingerprint replaced
        }

        /// <summary>Joined someone else's server: its list of charges (pictures come later, on approval).</summary>
        public void SetCatalogue(IEnumerable<ChargeIndex.Entry> entries)
        {
            Index.RemoveSource(ChargeSource.Server);
            foreach (var e in entries)
                Index.Add(e.Fingerprint, e.Pack, e.Names, e.Attitudes, ChargeSource.Server, e.Size);
            Generation++;
        }

        private List<ChargeBundle> ReadPacks(string folder, string what)
        {
            var result = new List<ChargeBundle>();
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Can't create {folder}: {ex.Message}");
                return result;
            }
            void Warn(string m) => Jotunn.Logger.LogWarning("[Heraldry] " + m);

            // Loose PNGs straight in the folder are one pack, named after the folder's purpose
            var loose = ReadFiles(folder, topOnly: true);
            if (loose.Count > 0) result.AddRange(PackReader.Read(loose, "my-charges", Warn));
            foreach (var dir in Directory.GetDirectories(folder).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try { result.AddRange(PackReader.Read(ReadFiles(dir, topOnly: false), Path.GetFileName(dir), Warn)); }
                catch (Exception ex) { Warn($"{Path.GetFileName(dir)}: {ex.Message}; skipped."); }
            }
            foreach (var zip in Directory.GetFiles(folder, "*.zip").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var files = Unzip(File.ReadAllBytes(zip), Path.GetFileName(zip));
                    if (files != null) result.AddRange(PackReader.Read(files, Path.GetFileNameWithoutExtension(zip), Warn));
                }
                catch (Exception ex) { Warn($"{Path.GetFileName(zip)}: {ex.Message}; skipped."); }
            }

            // Too big to send is still fine to use here, but say so
            foreach (var b in result.Where(b => b.Bytes.Length > HeraldryConfig.MaxChargeBytes))
                Warn($"[{b.Name}] ({b.Pack}) is {b.Bytes.Length >> 10} KB, over the {HeraldryConfig.MaxChargeBytes >> 10} KB limit: other players won't receive it.");
            return result;
        }

        private static Dictionary<string, byte[]> ReadFiles(string dir, bool topOnly)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            string root = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (var f in Directory.GetFiles(dir, "*", topOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".png" && ext != ".json") continue;
                if (topOnly && ext == ".json") continue; // a manifest belongs in a pack folder
                if (new FileInfo(f).Length > 16L << 20) continue;
                files[Path.GetFullPath(f).Substring(root.Length).Replace('\\', '/')] = File.ReadAllBytes(f);
            }
            return files;
        }

        private static readonly MiniZip.Limits ZipLimits = new MiniZip.Limits { MaxEntries = 512, MaxEntryBytes = 16L << 20, MaxTotalBytes = 64L << 20 };

        /// <summary>Unzips a pack; a single top folder ("test-beasts/manifest.json") is stripped.</summary>
        private static Dictionary<string, byte[]> Unzip(byte[] zip, string label)
        {
            var raw = MiniZip.Read(zip, ZipLimits);
            if (raw.ContainsKey("manifest.json") || !raw.Keys.Any(k => k.Contains("/"))) return raw;
            var tops = raw.Keys.Select(k => k.Split('/')[0]).Distinct().ToList();
            if (tops.Count != 1) return raw;
            string prefix = tops[0] + "/";
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in raw)
                if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    files[kv.Key.Substring(prefix.Length)] = kv.Value;
            return files;
        }

        private void Keep(ChargeBundle b, ChargeSource source)
        {
            bundles[b.Fingerprint] = b;
            Index.Add(b.Fingerprint, b.Pack, b.Names, b.Attitudes, source, b.Bytes.Length);
            if (pending.Remove(b.Fingerprint)) Generation++;
        }

        private void Forget(string fp)
        {
            bundles.Remove(fp);
            Index.Remove(fp);
            decoded.Remove(fp);
            ChargeArtLibrary.Unregister(ChargeBundle.ArtKey(fp));
        }

        // ================================================================== bundles

        public ChargeBundle Get(string fp) => fp != null && bundles.TryGetValue(fp, out var b) ? b : null;
        public bool IsServerFolder(string fp) => serverFolder.Contains(fp);
        public bool IsDownloaded(string fp) => downloaded.Contains(fp);

        /// <summary>A charge that arrived (checked by the caller): saved to Downloaded, usable at once. Main thread.</summary>
        public void AddDownloaded(ChargeBundle b)
        {
            try
            {
                Directory.CreateDirectory(DownloadFolder);
                File.WriteAllBytes(Path.Combine(DownloadFolder, b.Fingerprint + ".hcharge"), b.Bytes);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Couldn't save a downloaded charge: {ex.Message}");
            }
            var known = Index.Get(b.Fingerprint);
            Keep(b, known != null && known.Source == ChargeSource.Server ? ChargeSource.Server : ChargeSource.Downloaded);
            downloaded.Add(b.Fingerprint);
            Generation++;
        }

        /// <summary>Deletes a downloaded copy (never your own files). True if there was one.</summary>
        public bool RemoveDownloaded(string fp)
        {
            if (!downloaded.Remove(fp)) return false;
            try { File.Delete(Path.Combine(DownloadFolder, fp + ".hcharge")); }
            catch (Exception ex) { Jotunn.Logger.LogWarning($"[Heraldry] Couldn't delete a downloaded charge: {ex.Message}"); }
            var entry = Index.Get(fp);
            bundles.Remove(fp);
            decoded.Remove(fp);
            ChargeArtLibrary.Unregister(ChargeBundle.ArtKey(fp));
            if (entry != null && entry.Source == ChargeSource.Downloaded) Index.Remove(fp); // a server one stays listed
            Generation++;
            return true;
        }

        // ================================================================== drawing

        /// <summary>
        ///     Main thread, before arms are drawn (via IconArt.EnsureFor): decodes the pictures they use, and notes
        ///     the ones that aren't here (waiting list, reminder, or fetched at once if AutoGetServerCharges).
        /// </summary>
        public void EnsureFor(Blazon blazon)
        {
            if (blazon == null || !HasGraphics) return;
            foreach (var c in PackCharges(blazon))
            {
                string fp = c.Hash;
                if (fp == null || blocked.ContainsKey(fp) || blacklist.ContainsKey(fp)) continue;
                if (bundles.TryGetValue(fp, out var b))
                {
                    if (!decoded.Contains(fp)) Decode(b);
                    continue;
                }
                Missing(fp, c.PackName);
            }
        }

        private static IEnumerable<Charge> PackCharges(Blazon b)
        {
            foreach (var c in b.Charges) if (c.PackName != null) yield return c;
            foreach (var o in b.Ordinaries)
                foreach (var c in o.Charges) if (c.PackName != null) yield return c;
        }

        private void Missing(string fp, string name)
        {
            var entry = Index.Get(fp);
            bool fromServer = entry != null && entry.Source == ChargeSource.Server;
            if (!pending.TryGetValue(fp, out var w))
            {
                w = new Waiting { Fingerprint = fp, Name = entry?.Names.FirstOrDefault() ?? name, FromServer = fromServer, Size = entry?.Size ?? 0 };
                pending[fp] = w;
            }
            if (ChargeSync.IsRequested(fp)) return;
            if (fromServer && HeraldryConfig.AutoGetServerCharges)
            {
                ChargeSync.Request(new[] { fp });
                return;
            }
            ChargeSync.RemindEncounter(w.Name);
        }

        /// <summary>Server charges listed but not here, for the reminder on joining.</summary>
        public List<Waiting> MissingServerCharges()
        {
            foreach (var e in Index.All.Where(e => e.Source == ChargeSource.Server && !bundles.ContainsKey(e.Fingerprint)).ToList())
                if (!pending.ContainsKey(e.Fingerprint) && !blocked.ContainsKey(e.Fingerprint) && !blacklist.ContainsKey(e.Fingerprint))
                    pending[e.Fingerprint] = new Waiting { Fingerprint = e.Fingerprint, Name = e.Names.FirstOrDefault(), FromServer = true, Size = e.Size };
            return pending.Values.Where(w => w.FromServer && !bundles.ContainsKey(w.Fingerprint)).ToList();
        }

        private void Decode(ChargeBundle b)
        {
            decoded.Add(b.Fingerprint); // also when it fails: don't retry every frame
            var px = DecodePng(b.Image, out int w, out int h, b.Name);
            if (px == null) return;
            Rgba[] mask = null;
            if (b.Mask != null)
            {
                mask = DecodePng(b.Mask, out int mw, out int mh, b.Name + " mask");
                if (mask != null && (mw != w || mh != h)) mask = null;
            }
            ChargeArtLibrary.Register(ChargeBundle.ArtKey(b.Fingerprint), px, w, h, deriveLines: !b.NoLines, mask: mask);
            // No Generation bump: this runs right before the arms are drawn, so nothing already shown needs redrawing
        }

        private static Rgba[] DecodePng(byte[] bytes, out int w, out int h, string label)
        {
            w = h = 0;
            if (!MiniPng.Size(bytes, out w, out h) || w < 1 || h < 1 || w > PackReader.MaxChargeSide || h > PackReader.MaxChargeSide)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Charge '{label}': not a PNG within {PackReader.MaxChargeSide} px; shown as the placeholder.");
                return null;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!AssetUtils.LoadImage(tex, bytes) || tex.width != w || tex.height != h)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] Charge '{label}' couldn't be decoded; shown as the placeholder.");
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
                return px;
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        // ================================================================== block list and blacklist

        public bool IsBlocked(string fp) => fp != null && blocked.ContainsKey(fp);
        public bool IsBlacklisted(string fp) => fp != null && blacklist.ContainsKey(fp);
        public IReadOnlyDictionary<string, string> Blocked => blocked;
        public IReadOnlyDictionary<string, string> Blacklist => blacklist;

        /// <summary>For ChargeArtLibrary: a "charge:" art key that must show as the placeholder.</summary>
        public bool IsArtKeyHidden(string key)
        {
            if (key == null || !key.StartsWith(ChargeBundle.ArtKeyPrefix, StringComparison.Ordinal)) return false;
            return hidden.Contains(key.Substring(ChargeBundle.ArtKeyPrefix.Length)); // a snapshot: renders run on worker threads
        }

        private volatile HashSet<string> hidden = new HashSet<string>();

        private void Changed()
        {
            hidden = new HashSet<string>(blocked.Keys.Concat(blacklist.Keys));
            Generation++;
        }

        public void Block(string fp, string name)
        {
            blocked[fp] = name ?? "";
            pending.Remove(fp);
            SaveBlocked();
            Changed();
        }

        public bool Unblock(string fp)
        {
            if (!blocked.Remove(fp)) return false;
            SaveBlocked();
            Changed();
            return true;
        }

        private void LoadBlocked()
        {
            blocked.Clear();
            foreach (var kv in ReadList(BlockFile)) blocked[kv.Key] = kv.Value;
            Changed();
        }

        private void SaveBlocked() => WriteList(BlockFile, blocked, "Charges you've blocked (heraldry_block): fingerprint, then name.");

        /// <summary>Server side: the admins' blacklist from its file.</summary>
        public void LoadBlacklistFile()
        {
            blacklist.Clear();
            foreach (var kv in ReadList(BlacklistFile)) blacklist[kv.Key] = kv.Value;
            Changed();
        }

        public void SaveBlacklistFile() => WriteList(BlacklistFile, blacklist, "Charges blacklisted on this server (heraldry_blacklist): fingerprint, then name.");

        /// <summary>Server side: change the blacklist. True if it changed.</summary>
        public bool SetBlacklisted(string fp, string name, bool on)
        {
            bool changed = on ? !blacklist.ContainsKey(fp) : blacklist.ContainsKey(fp);
            if (on) blacklist[fp] = name ?? "";
            else blacklist.Remove(fp);
            if (changed) { SaveBlacklistFile(); Changed(); }
            return changed;
        }

        /// <summary>Client side: the server's blacklist, as it sent it.</summary>
        public void ReplaceBlacklist(Dictionary<string, string> list)
        {
            blacklist.Clear();
            foreach (var kv in list) blacklist[kv.Key] = kv.Value;
            foreach (var fp in list.Keys) pending.Remove(fp);
            Changed();
        }

        private static IEnumerable<KeyValuePair<string, string>> ReadList(string path)
        {
            var result = new List<KeyValuePair<string, string>>();
            try
            {
                if (!File.Exists(path)) return result;
                foreach (var line in File.ReadAllLines(path))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    var parts = t.Split(new[] { ' ', '\t' }, 2);
                    string fp = parts[0].ToLowerInvariant();
                    if (BlazonText.IsFingerprint(fp)) result.Add(new KeyValuePair<string, string>(fp, parts.Length > 1 ? parts[1].Trim() : ""));
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Couldn't read {path}: {ex.Message}");
            }
            return result;
        }

        private static void WriteList(string path, Dictionary<string, string> list, string header)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, new[] { "# " + header }.Concat(list.Select(kv => $"{kv.Key} {kv.Value}")));
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Couldn't write {path}: {ex.Message}");
            }
        }

        // ================================================================== IPackCharges (for the parser)

        public string Resolve(string name, IList<string> attitudes, out string unmatched)
        {
            Index.OwnFirst = HeraldryConfig.OwnChargesFirst;
            return Index.Resolve(name, attitudes, out unmatched);
        }

        public bool Has(string fp) => fp != null && bundles.ContainsKey(fp);
        public string Suggest(string name) => Index.Suggest(name);

        /// <summary>Every charge name in use (basic mode's list).</summary>
        public List<string> Names() => Index.Names();
    }
}
