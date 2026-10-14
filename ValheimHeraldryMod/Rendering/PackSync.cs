using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     Sends a server's charge packs to the players who want them.
    ///     Trust is per pack, by fingerprint (SHA-256 of the zip): it doesn't matter who sends a pack, and any
    ///     change to it means a new fingerprint, so players are asked again (Hippo: re-ask on any change).
    ///     Flow: client says hello -> server offers {name, version, charges, size, fingerprint} -> client loads
    ///     allowed packs from its cache or requests them -> server sends the zips (Jötunn slices and compresses
    ///     big packages) -> client checks size and fingerprint, caches, loads, and banners redraw.
    /// </summary>
    internal static class PackSync
    {
        private const string RpcName = "Heraldry_ChargePacks";
        private const byte Hello = 1, OfferMsg = 2, Request = 3, PackMsg = 4;
        private const int ProtocolVersion = 1;

        public sealed class Offer
        {
            public string Name;
            public int Version;
            public int Charges;
            public long Size;
            public string Fingerprint;
            public string Status = "offered";
        }

        private static CustomRPC rpc;
        private static readonly List<Offer> offers = new List<Offer>();
        private static readonly HashSet<string> requested = new HashSet<string>();
        private static bool joined;      // on someone else's server, hello sent
        private static long received;    // bytes received from this server this session

        public static IReadOnlyList<Offer> Offers => offers;
        public static bool OnRemoteServer => joined;

        public static void Init()
        {
            rpc = NetworkManager.Instance.AddRPC(RpcName, OnServerReceive, OnClientReceive);
        }

        /// <summary>From the plugin's Update: notices joining and leaving someone else's server.</summary>
        public static void Update()
        {
            var znet = ZNet.instance;
            bool remote = znet != null && !znet.IsServer() && znet.GetServerPeer() != null && znet.GetServerPeer().IsReady();
            if (remote && !joined)
            {
                joined = true;
                received = 0;
                offers.Clear();
                requested.Clear();
                ChargePacks.Instance.UseServerPacks(true); // the server's packs only, from now on
                var pkg = new ZPackage();
                pkg.Write(Hello);
                pkg.Write(ProtocolVersion);
                rpc.SendPackage(ZNet.instance.GetServerPeer().m_uid, pkg);
            }
            else if (joined && znet == null)
            {
                joined = false;
                offers.Clear();
                requested.Clear();
                ChargePacks.Instance.UseServerPacks(false); // back to this computer's packs
            }
        }

        // ------------------------------------------------------------------ server side

        private static IEnumerator OnServerReceive(long sender, ZPackage pkg)
        {
            byte type = pkg.ReadByte();
            if (type == Hello)
            {
                int version = pkg.ReadInt();
                if (version != ProtocolVersion) yield break;
                var reply = new ZPackage();
                reply.Write(OfferMsg);
                var served = Served();
                reply.Write(served.Count);
                foreach (var b in served)
                {
                    reply.Write(b.Name);
                    reply.Write(b.Version);
                    reply.Write(b.Charges);
                    reply.Write((long)b.Zip.Length);
                    reply.Write(b.Fingerprint);
                }
                rpc.SendPackage(sender, reply);
            }
            else if (type == Request)
            {
                int n = Math.Min(pkg.ReadInt(), 256);
                var wanted = new HashSet<string>();
                for (int i = 0; i < n; i++) wanted.Add(pkg.ReadString());
                foreach (var b in Served())
                {
                    if (!wanted.Contains(b.Fingerprint)) continue;
                    var p = new ZPackage();
                    p.Write(PackMsg);
                    p.Write(b.Fingerprint);
                    p.Write(b.Name);
                    p.Write(b.Zip);
                    rpc.SendPackage(sender, p);
                }
            }
            yield break;
        }

        /// <summary>The local packs this server will send, within the admin's limits.</summary>
        private static List<ChargePacks.Bundle> Served()
        {
            var list = new List<ChargePacks.Bundle>();
            long total = 0;
            foreach (var b in ChargePacks.Instance.LocalBundles)
            {
                if (b.Zip.Length > HeraldryConfig.MaxPackBytes)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] Charge pack '{b.Name}' is {b.Zip.Length >> 10} KB zipped, over MaxChargePackMB; not sent to players.");
                    continue;
                }
                if (total + b.Zip.Length > HeraldryConfig.MaxServedBytes)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] Charge pack '{b.Name}' would take the total over MaxChargePacksTotalMB; not sent to players.");
                    continue;
                }
                total += b.Zip.Length;
                list.Add(b);
            }
            return list;
        }

        // ------------------------------------------------------------------ client side

        private static IEnumerator OnClientReceive(long sender, ZPackage pkg)
        {
            byte type = pkg.ReadByte();
            if (type == OfferMsg) ReadOffer(pkg);
            else if (type == PackMsg) ReadPack(pkg);
            yield break;
        }

        private static void ReadOffer(ZPackage pkg)
        {
            offers.Clear();
            int n = Math.Min(pkg.ReadInt(), 256);
            for (int i = 0; i < n; i++)
                offers.Add(new Offer
                {
                    Name = pkg.ReadString(),
                    Version = pkg.ReadInt(),
                    Charges = pkg.ReadInt(),
                    Size = pkg.ReadLong(),
                    Fingerprint = pkg.ReadString().ToLowerInvariant(),
                });
            if (offers.Count == 0) return;

            if (!HeraldryConfig.ReceiveServerPacks)
            {
                foreach (var o in offers) o.Status = "not received (ReceiveServerPacks is off)";
                Tell($"This server has {offers.Count} charge pack(s) ({Mb(offers.Sum(o => o.Size))}). " +
                     "Turn on ReceiveServerPacks in the Heraldry config to see their charges.");
                return;
            }
            Fetch();
        }

        /// <summary>Loads allowed packs from the cache, requests the rest, and asks about new ones.</summary>
        public static void Fetch()
        {
            if (!joined || !HeraldryConfig.ReceiveServerPacks) return;
            var toRequest = new List<string>();
            var untrusted = new List<Offer>();
            foreach (var o in offers)
            {
                if (o.Status == "loaded" || requested.Contains(o.Fingerprint)) continue;
                if (!TrustStore.IsTrusted(o.Fingerprint))
                {
                    o.Status = "waiting for heraldry_trust";
                    untrusted.Add(o);
                    continue;
                }
                var cached = PackCache.Load(o.Fingerprint);
                if (cached != null)
                {
                    o.Status = ChargePacks.Instance.LoadServerPack(cached, o.Name) ? "loaded" : "failed to load";
                    continue;
                }
                if (received + o.Size > HeraldryConfig.MaxDownloadBytes)
                {
                    o.Status = "skipped (over MaxChargePackDownloadMB)";
                    Tell($"Charge pack '{o.Name}' ({Mb(o.Size)}) would go over your MaxChargePackDownloadMB; not downloaded.");
                    continue;
                }
                o.Status = "downloading";
                requested.Add(o.Fingerprint);
                toRequest.Add(o.Fingerprint);
            }
            if (toRequest.Count > 0)
            {
                var pkg = new ZPackage();
                pkg.Write(Request);
                pkg.Write(toRequest.Count);
                foreach (var f in toRequest) pkg.Write(f);
                rpc.SendPackage(ZNet.instance.GetServerPeer().m_uid, pkg);
            }
            if (untrusted.Count > 0)
            {
                string list = string.Join(", ", untrusted.Select(o => $"'{o.Name}' v{o.Version} ({o.Charges} charges, {Mb(o.Size)})"));
                Tell($"This server offers charge pack(s) you haven't allowed (new or changed): {list}. " +
                     $"Type 'heraldry_trust {untrusted[0].Name}' in the console (F5) to allow one, or 'heraldry_trust all'.");
            }
        }

        private static void ReadPack(ZPackage pkg)
        {
            string fingerprint = pkg.ReadString().ToLowerInvariant();
            string name = pkg.ReadString();
            byte[] zip = pkg.ReadByteArray();
            var offer = offers.FirstOrDefault(o => o.Fingerprint == fingerprint);
            if (offer == null || !requested.Contains(fingerprint) || !TrustStore.IsTrusted(fingerprint))
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Ignored charge pack '{name}' from the server: it wasn't requested.");
                return;
            }
            requested.Remove(fingerprint);
            received += zip.Length;
            if (zip.Length != offer.Size || MiniZip.Fingerprint(zip) != fingerprint)
            {
                offer.Status = "failed (didn't match its fingerprint)";
                Tell($"Charge pack '{offer.Name}' arrived damaged or different from what was offered; ignored.");
                return;
            }
            PackCache.Save(fingerprint, zip);
            bool ok = ChargePacks.Instance.LoadServerPack(zip, offer.Name);
            offer.Status = ok ? "loaded" : "failed to load";
            Tell(ok ? $"Charge pack '{offer.Name}' loaded." : $"Charge pack '{offer.Name}' couldn't be loaded; see the log.");
        }

        // ------------------------------------------------------------------ helpers

        internal static void Tell(string text)
        {
            Jotunn.Logger.LogInfo("[Heraldry] " + text);
            global::Console.instance?.Print("[Heraldry] " + text);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }

        internal static string Mb(long bytes) => bytes < 1 << 20 ? $"{Math.Max(1, bytes >> 10)} KB" : $"{bytes / (double)(1 << 20):0.0} MB";
    }

    /// <summary>
    ///     Fingerprints of the charge packs this player has allowed: BepInEx/config/Heraldry/trusted_packs.txt,
    ///     one "fingerprint name version" per line.
    /// </summary>
    internal static class TrustStore
    {
        private static Dictionary<string, string> trusted;

        private static string FilePath => Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "Heraldry"), "trusted_packs.txt");

        private static Dictionary<string, string> All
        {
            get
            {
                if (trusted != null) return trusted;
                trusted = new Dictionary<string, string>();
                try
                {
                    if (File.Exists(FilePath))
                        foreach (var line in File.ReadAllLines(FilePath))
                        {
                            var parts = line.Trim().Split(new[] { ' ' }, 2);
                            if (parts[0].Length == 64) trusted[parts[0].ToLowerInvariant()] = parts.Length > 1 ? parts[1] : "";
                        }
                }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] Couldn't read {FilePath}: {ex.Message}");
                }
                return trusted;
            }
        }

        public static bool IsTrusted(string fingerprint) => All.ContainsKey(fingerprint);

        public static void Trust(PackSync.Offer o)
        {
            All[o.Fingerprint] = $"{o.Name} v{o.Version}";
            Save();
        }

        public static int UntrustByName(string name)
        {
            var gone = All.Where(kv => name == "all" || kv.Value.Split(' ')[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key).ToList();
            foreach (var k in gone) All.Remove(k);
            Save();
            return gone.Count;
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, All.Select(kv => $"{kv.Key} {kv.Value}"));
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Couldn't write {FilePath}: {ex.Message}");
            }
        }
    }

    /// <summary>Downloaded packs by fingerprint (BepInEx/cache/Heraldry/packs), shared by every server.</summary>
    internal static class PackCache
    {
        private static string Folder => Path.Combine(Path.Combine(BepInEx.Paths.CachePath, "Heraldry"), "packs");

        public static byte[] Load(string fingerprint)
        {
            try
            {
                string path = Path.Combine(Folder, fingerprint + ".zip");
                if (!File.Exists(path)) return null;
                var zip = File.ReadAllBytes(path);
                return MiniZip.Fingerprint(zip) == fingerprint ? zip : null; // a damaged file is simply downloaded again
            }
            catch
            {
                return null;
            }
        }

        public static void Save(string fingerprint, byte[] zip)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(Path.Combine(Folder, fingerprint + ".zip"), zip);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Couldn't cache a charge pack: {ex.Message}");
            }
        }
    }
}
