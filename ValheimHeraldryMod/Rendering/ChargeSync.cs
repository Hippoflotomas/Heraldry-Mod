using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     Charges between players (2026-10-14 rework; replaces the per-pack PackSync).
    ///     <list type="bullet">
    ///         <item>Joining: the server sends the list of its own charges (names, fingerprints, sizes) and its
    ///         blacklist. A reminder says how many you haven't got (once, on joining).</item>
    ///         <item>Approving (heraldry_get, or at once with AutoGetServerCharges for the server's own): the client
    ///         asks the server for fingerprints. The server answers from what it holds (its folder, its relay
    ///         memory) or asks the other players; the first copy that checks out is passed on to everyone waiting
    ///         and kept in memory (RelayCacheMB, never on disk) so it isn't fetched twice.</item>
    ///         <item>A blacklisted charge is never passed on, and clients draw it as the placeholder.</item>
    ///     </list>
    ///     Every charge is checked against its fingerprint and the size limits by whoever receives it.
    ///     When this computer is the server (hosting, single player) its own requests are handled in-process.
    /// </summary>
    internal static class ChargeSync
    {
        private const string RpcName = "ValheimHeraldryMod_Charges";
        private const int ProtocolVersion = 2;
        private const long Local = long.MinValue; // "this computer" as the requester, when it's also the server

        // Client -> server
        private const byte Hello = 1, Want = 2, Offer = 3, AdminOp = 4;
        // Server -> client
        private const byte Catalogue = 10, ChargeMsg = 11, Unavailable = 12, Ask = 13, BlacklistMsg = 14, Reply = 15;
        // Admin operations
        public const byte OpBlacklist = 1, OpUnblacklist = 2, OpReload = 3, OpList = 4;

        private enum State { None, Server, Remote }

        private static CustomRPC rpc;
        private static State state;

        // client
        private static readonly Dictionary<string, float> requested = new Dictionary<string, float>(); // fingerprint -> when
        private static long received, uploaded;
        private static float lastEncounterReminder = -999f;
        private static readonly HashSet<string> encountered = new HashSet<string>();

        // server
        private static readonly Dictionary<string, HashSet<long>> waiters = new Dictionary<string, HashSet<long>>();
        private static readonly Dictionary<string, float> asked = new Dictionary<string, float>();
        private static readonly Dictionary<string, byte[]> relay = new Dictionary<string, byte[]>();
        private static readonly LinkedList<string> relayOrder = new LinkedList<string>();
        private static long relayBytes;
        private static readonly Dictionary<long, long> uploadsByPeer = new Dictionary<long, long>();

        public static bool OnRemoteServer => state == State.Remote;

        public static void Init()
        {
            rpc = NetworkManager.Instance.AddRPC(RpcName, OnServerReceive, OnClientReceive);
        }

        /// <summary>From the plugin's Update: notices starting, joining and leaving games.</summary>
        public static void Update()
        {
            var znet = ZNet.instance;
            if (znet == null)
            {
                if (state != State.None) Reset();
                return;
            }
            if (znet.IsServer())
            {
                if (state == State.Server) return;
                state = State.Server;
                ChargeLibrary.Instance.LoadServerFolder();
                return;
            }
            var peer = znet.GetServerPeer();
            if (state == State.None && peer != null && peer.IsReady())
            {
                state = State.Remote;
                received = uploaded = 0;
                var pkg = new ZPackage();
                pkg.Write(Hello);
                pkg.Write(ProtocolVersion);
                rpc.SendPackage(peer.m_uid, pkg);
            }
        }

        private static void Reset()
        {
            state = State.None;
            requested.Clear();
            encountered.Clear();
            waiters.Clear();
            asked.Clear();
            relay.Clear();
            relayOrder.Clear();
            relayBytes = 0;
            uploadsByPeer.Clear();
            ChargeLibrary.Instance.LeaveServer();
        }

        // ================================================================== client: asking

        public static bool IsRequested(string fp) => requested.TryGetValue(fp, out float t) && Time.time - t < 60f;

        /// <summary>Asks for these pictures (approved ones). Returns how many were asked for.</summary>
        public static int Request(IEnumerable<string> fingerprints)
        {
            var lib = ChargeLibrary.Instance;
            var list = new List<string>();
            foreach (var fp in fingerprints)
            {
                if (lib.Has(fp) || IsRequested(fp) || lib.IsBlacklisted(fp)) continue;
                long size = lib.Index.Get(fp)?.Size ?? 0;
                if (received + size > HeraldryConfig.MaxDownloadBytes)
                {
                    Tell($"Not fetched: it would take you over MaxChargeDownloadMB this session ({Mb(HeraldryConfig.MaxDownloadBytes)}).");
                    break;
                }
                requested[fp] = Time.time;
                SetStatus(fp, "fetching");
                list.Add(fp);
            }
            if (list.Count == 0) return 0;
            if (state == State.Server) ServerWant(Local, list);
            else if (state == State.Remote)
            {
                var pkg = new ZPackage();
                pkg.Write(Want);
                pkg.Write(list.Count);
                foreach (var fp in list) pkg.Write(fp);
                rpc.SendPackage(ZNet.instance.GetServerPeer().m_uid, pkg);
            }
            else
            {
                foreach (var fp in list) { requested.Remove(fp); SetStatus(fp, "not in a game"); }
                return 0;
            }
            return list.Count;
        }

        /// <summary>An admin command for the server (blacklist, reload...). The server checks the sender is an admin.</summary>
        public static void Admin(byte op, string fingerprint, string name)
        {
            if (state == State.Server) { ServerAdmin(Local, op, fingerprint ?? "", name ?? ""); return; }
            if (state != State.Remote) { Tell("Not in a game."); return; }
            var pkg = new ZPackage();
            pkg.Write(AdminOp);
            pkg.Write(op);
            pkg.Write(fingerprint ?? "");
            pkg.Write(name ?? "");
            rpc.SendPackage(ZNet.instance.GetServerPeer().m_uid, pkg);
        }

        /// <summary>
        ///     The reminder when arms show a charge you haven't got (Hippo: every time, even one seen before;
        ///     several at once are gathered into one message, at most one every few seconds).
        /// </summary>
        public static void RemindEncounter(string name)
        {
            encountered.Add(name ?? "?");
            if (Time.time - lastEncounterReminder < 8f) return;
            lastEncounterReminder = Time.time;
            string names = string.Join(", ", encountered.Take(4).Select(n => "[" + n + "]")) + (encountered.Count > 4 ? ", ..." : "");
            encountered.Clear();
            Tell($"Arms here use charge pictures you haven't got: {names}. Type 'heraldry_pending' in the console (F5) to see them, 'heraldry_get all' to fetch them.");
        }

        // ================================================================== client: receiving

        private static IEnumerator OnClientReceive(long sender, ZPackage pkg)
        {
            byte type = pkg.ReadByte();
            try
            {
                switch (type)
                {
                    case Catalogue: ReadCatalogue(pkg); break;
                    case ChargeMsg: ReceiveCharge(pkg.ReadString(), pkg.ReadByteArray()); break;
                    case Unavailable: Unavailable_(pkg.ReadString(), pkg.ReadString()); break;
                    case Ask: AnswerAsk(pkg); break;
                    case BlacklistMsg: ChargeLibrary.Instance.ReplaceBlacklist(ReadNamedList(pkg)); break;
                    case Reply: Tell(pkg.ReadString()); break;
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Bad charge message from the server: {ex.Message}");
            }
            yield break;
        }

        private static void ReadCatalogue(ZPackage pkg)
        {
            int n = Math.Min(pkg.ReadInt(), 5000);
            var entries = new List<ChargeIndex.Entry>();
            for (int i = 0; i < n; i++)
            {
                var e = new ChargeIndex.Entry { Fingerprint = pkg.ReadString(), Pack = pkg.ReadString() };
                e.Names = ReadStrings(pkg);
                e.Attitudes = ReadStrings(pkg);
                e.Size = pkg.ReadLong();
                if (BlazonText.IsFingerprint(e.Fingerprint)) entries.Add(e);
            }
            var lib = ChargeLibrary.Instance;
            lib.SetCatalogue(entries);
            lib.ReplaceBlacklist(ReadNamedList(pkg));

            // The reminder on joining (once): the server's charges you haven't got
            var missing = lib.MissingServerCharges();
            if (missing.Count == 0) return;
            if (HeraldryConfig.AutoGetServerCharges)
            {
                Request(missing.Select(w => w.Fingerprint));
                return;
            }
            long size = missing.Sum(w => w.Size);
            ShowCentre($"This server has {missing.Count} charge picture(s) you haven't got ({Mb(size)}).\n" +
                       "Type 'heraldry_pending' in the console (F5) to see them, 'heraldry_get all' to fetch them.");
        }

        private static void ReceiveCharge(string fp, byte[] bytes)
        {
            fp = fp.ToLowerInvariant();
            if (!requested.Remove(fp))
            {
                Jotunn.Logger.LogWarning("[Heraldry] Ignored a charge picture that wasn't asked for.");
                return;
            }
            received += bytes.Length;
            ChargeBundle b;
            try
            {
                b = ChargeBundle.Read(bytes, HeraldryConfig.MaxChargeBytes);
            }
            catch (System.IO.InvalidDataException ex)
            {
                SetStatus(fp, "refused: " + ex.Message);
                Tell($"A charge picture was refused: {ex.Message}.");
                return;
            }
            if (b.Fingerprint != fp)
            {
                SetStatus(fp, "arrived damaged");
                Tell("A charge picture arrived different from what was asked for; ignored.");
                return;
            }
            ChargeLibrary.Instance.AddDownloaded(b);
            Tell($"Got the charge [{b.Name}].");
        }

        private static void Unavailable_(string fp, string why)
        {
            requested.Remove(fp);
            SetStatus(fp, why);
            var w = ChargeLibrary.Instance.Pending.FirstOrDefault(p => p.Fingerprint == fp);
            Tell($"Charge [{w?.Name ?? fp}]: {why}.");
        }

        /// <summary>The server asks who has these pictures: send the ones this computer has (if sharing is on).</summary>
        private static void AnswerAsk(ZPackage pkg)
        {
            int n = Math.Min(pkg.ReadInt(), 64);
            var lib = ChargeLibrary.Instance;
            for (int i = 0; i < n; i++)
            {
                string fp = pkg.ReadString();
                if (!HeraldryConfig.ShareCharges) continue;
                var b = lib.Get(fp);
                if (b == null || lib.IsBlacklisted(fp) || b.Bytes.Length > HeraldryConfig.MaxChargeBytes) continue;
                if (uploaded + b.Bytes.Length > HeraldryConfig.MaxUploadBytes) continue;
                uploaded += b.Bytes.Length;
                var reply = new ZPackage();
                reply.Write(Offer);
                reply.Write(fp);
                reply.Write(b.Bytes);
                rpc.SendPackage(ZNet.instance.GetServerPeer().m_uid, reply);
            }
        }

        private static void SetStatus(string fp, string status)
        {
            var w = ChargeLibrary.Instance.Pending.FirstOrDefault(p => p.Fingerprint == fp);
            if (w != null) w.Status = status;
        }

        // ================================================================== server

        private static IEnumerator OnServerReceive(long sender, ZPackage pkg)
        {
            byte type = pkg.ReadByte();
            try
            {
                switch (type)
                {
                    case Hello:
                        if (pkg.ReadInt() == ProtocolVersion) Welcome(sender);
                        break;
                    case Want:
                    {
                        int n = Math.Min(pkg.ReadInt(), 256);
                        var list = new List<string>();
                        for (int i = 0; i < n; i++) list.Add(pkg.ReadString().ToLowerInvariant());
                        ServerWant(sender, list);
                        break;
                    }
                    case Offer: ServerOffer(sender, pkg.ReadString().ToLowerInvariant(), pkg.ReadByteArray()); break;
                    case AdminOp: ServerAdmin(sender, pkg.ReadByte(), pkg.ReadString().ToLowerInvariant(), pkg.ReadString()); break;
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Bad charge message from a player: {ex.Message}");
            }
            yield break;
        }

        private static void Welcome(long peer)
        {
            var lib = ChargeLibrary.Instance;
            var pkg = new ZPackage();
            pkg.Write(Catalogue);
            var served = lib.Index.All
                .Where(e => lib.IsServerFolder(e.Fingerprint) && !lib.IsBlacklisted(e.Fingerprint))
                .Where(e => e.Size <= HeraldryConfig.ServerMaxChargeBytes).ToList();
            pkg.Write(served.Count);
            foreach (var e in served)
            {
                pkg.Write(e.Fingerprint);
                pkg.Write(e.Pack ?? "");
                WriteStrings(pkg, e.Names);
                WriteStrings(pkg, e.Attitudes);
                pkg.Write(e.Size);
            }
            WriteNamedList(pkg, lib.Blacklist);
            rpc.SendPackage(peer, pkg);

            // Someone new might have what others are waiting for
            var wanted = waiters.Keys.ToList();
            if (wanted.Count > 0) SendAsk(peer, wanted);
        }

        private static void ServerWant(long peer, List<string> fingerprints)
        {
            var lib = ChargeLibrary.Instance;
            var toAsk = new List<string>();
            foreach (var fp in fingerprints)
            {
                if (!BlazonText.IsFingerprint(fp)) continue;
                if (lib.IsBlacklisted(fp)) { SendUnavailable(peer, fp, "blacklisted on this server"); continue; }
                var b = lib.Get(fp);
                byte[] bytes = b != null && b.Bytes.Length <= HeraldryConfig.ServerMaxChargeBytes ? b.Bytes
                             : relay.TryGetValue(fp, out var r) ? r : null;
                if (bytes != null) { SendCharge(peer, fp, bytes); continue; }
                if (!waiters.TryGetValue(fp, out var set)) waiters[fp] = set = new HashSet<long>();
                set.Add(peer);
                if (!asked.TryGetValue(fp, out float t) || Time.time - t > 20f)
                {
                    asked[fp] = Time.time;
                    toAsk.Add(fp);
                }
            }
            if (toAsk.Count == 0) return;
            var peers = ZNet.instance.GetPeers().Where(p => p.m_uid != peer).ToList();
            foreach (var p in peers) SendAsk(p.m_uid, toAsk);
            if (peers.Count == 0)
                foreach (var fp in toAsk) NobodyHasIt(fp);
            // Nobody answered in time: tell the ones waiting (they can ask again later)
            ValheimHeraldryMod.Instance.StartCoroutine(GiveUp(toAsk, 20f));
        }

        private static IEnumerator GiveUp(List<string> fingerprints, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            foreach (var fp in fingerprints)
                if (waiters.ContainsKey(fp)) NobodyHasIt(fp);
        }

        private static void NobodyHasIt(string fp)
        {
            if (!waiters.TryGetValue(fp, out var set)) return;
            waiters.Remove(fp);
            foreach (var peer in set) SendUnavailable(peer, fp, "nobody online has it right now; try 'heraldry_get' again later");
        }

        private static void ServerOffer(long peer, string fp, byte[] bytes)
        {
            if (!waiters.ContainsKey(fp)) return; // not wanted (any more)
            var lib = ChargeLibrary.Instance;
            uploadsByPeer.TryGetValue(peer, out long up);
            if (up + bytes.Length > HeraldryConfig.ServerMaxUploadBytes) return;
            uploadsByPeer[peer] = up + bytes.Length;
            if (lib.IsBlacklisted(fp)) return;
            try
            {
                var b = ChargeBundle.Read(bytes, HeraldryConfig.ServerMaxChargeBytes);
                if (b.Fingerprint != fp) return;
            }
            catch (System.IO.InvalidDataException)
            {
                return;
            }
            Remember(fp, bytes);
            var set = waiters[fp];
            waiters.Remove(fp);
            foreach (var w in set) SendCharge(w, fp, bytes);
        }

        /// <summary>Relay memory: recently passed-on charges, up to RelayCacheMB, oldest dropped first. Never on disk.</summary>
        private static void Remember(string fp, byte[] bytes)
        {
            if (relay.ContainsKey(fp)) return;
            long max = HeraldryConfig.RelayCacheBytes;
            if (bytes.Length > max) return;
            while (relayBytes + bytes.Length > max && relayOrder.Count > 0)
            {
                string old = relayOrder.First.Value;
                relayOrder.RemoveFirst();
                relayBytes -= relay[old].Length;
                relay.Remove(old);
            }
            relay[fp] = bytes;
            relayOrder.AddLast(fp);
            relayBytes += bytes.Length;
        }

        private static void ServerAdmin(long peer, byte op, string fp, string name)
        {
            if (!IsAdmin(peer))
            {
                SendReply(peer, "Only admins can do that on this server.");
                return;
            }
            var lib = ChargeLibrary.Instance;
            switch (op)
            {
                case OpBlacklist:
                case OpUnblacklist:
                    if (!BlazonText.IsFingerprint(fp)) { SendReply(peer, "That isn't a charge fingerprint."); return; }
                    bool on = op == OpBlacklist;
                    bool changed = lib.SetBlacklisted(fp, name, on);
                    if (on) { relay.Remove(fp); relayOrder.Remove(fp); waiters.Remove(fp); }
                    if (changed) BroadcastBlacklist();
                    SendReply(peer, changed ? $"[{name}] ({fp}) {(on ? "blacklisted" : "taken off the blacklist")}."
                                            : $"[{name}] ({fp}) was {(on ? "already" : "not")} on the blacklist.");
                    break;
                case OpReload:
                    lib.LoadServerFolder();
                    foreach (var p in ZNet.instance.GetPeers()) Welcome(p.m_uid); // everyone gets the new list
                    SendReply(peer, $"Server charges reloaded: {lib.Index.All.Count(e => lib.IsServerFolder(e.Fingerprint))} charge(s).");
                    break;
                case OpList:
                    SendReply(peer, lib.Blacklist.Count == 0 ? "The blacklist is empty."
                        : "Blacklisted: " + string.Join("; ", lib.Blacklist.Select(kv => $"[{kv.Value}] {kv.Key}")));
                    break;
            }
        }

        private static bool IsAdmin(long peer)
        {
            if (peer == Local) return true; // the host
            var p = ZNet.instance.GetPeer(peer);
            return p != null && ZNet.instance.IsAdmin(p.m_rpc.GetSocket().GetHostName());
        }

        private static void BroadcastBlacklist()
        {
            foreach (var p in ZNet.instance.GetPeers())
            {
                var pkg = new ZPackage();
                pkg.Write(BlacklistMsg);
                WriteNamedList(pkg, ChargeLibrary.Instance.Blacklist);
                rpc.SendPackage(p.m_uid, pkg);
            }
        }

        // ---- sending (to a peer, or straight back to this computer when it's the requester)

        private static void SendCharge(long peer, string fp, byte[] bytes)
        {
            if (peer == Local) { ReceiveCharge(fp, bytes); return; }
            var pkg = new ZPackage();
            pkg.Write(ChargeMsg);
            pkg.Write(fp);
            pkg.Write(bytes);
            rpc.SendPackage(peer, pkg);
        }

        private static void SendUnavailable(long peer, string fp, string why)
        {
            if (peer == Local) { Unavailable_(fp, why); return; }
            var pkg = new ZPackage();
            pkg.Write(Unavailable);
            pkg.Write(fp);
            pkg.Write(why);
            rpc.SendPackage(peer, pkg);
        }

        private static void SendReply(long peer, string text)
        {
            if (peer == Local) { Tell(text); return; }
            var pkg = new ZPackage();
            pkg.Write(Reply);
            pkg.Write(text);
            rpc.SendPackage(peer, pkg);
        }

        private static void SendAsk(long peer, List<string> fingerprints)
        {
            var pkg = new ZPackage();
            pkg.Write(Ask);
            pkg.Write(Math.Min(fingerprints.Count, 64));
            foreach (var fp in fingerprints.Take(64)) pkg.Write(fp);
            rpc.SendPackage(peer, pkg);
        }

        // ================================================================== helpers

        private static void WriteStrings(ZPackage pkg, string[] a)
        {
            pkg.Write(a?.Length ?? 0);
            if (a != null) foreach (var s in a) pkg.Write(s ?? "");
        }

        private static string[] ReadStrings(ZPackage pkg)
        {
            int n = Math.Min(pkg.ReadInt(), 64);
            var a = new string[n];
            for (int i = 0; i < n; i++) a[i] = pkg.ReadString();
            return a;
        }

        private static void WriteNamedList(ZPackage pkg, IReadOnlyDictionary<string, string> list)
        {
            pkg.Write(list.Count);
            foreach (var kv in list) { pkg.Write(kv.Key); pkg.Write(kv.Value ?? ""); }
        }

        private static Dictionary<string, string> ReadNamedList(ZPackage pkg)
        {
            int n = Math.Min(pkg.ReadInt(), 10000);
            var d = new Dictionary<string, string>();
            for (int i = 0; i < n; i++)
            {
                string fp = pkg.ReadString().ToLowerInvariant(), name = pkg.ReadString();
                if (BlazonText.IsFingerprint(fp)) d[fp] = name;
            }
            return d;
        }

        internal static void Tell(string text)
        {
            Jotunn.Logger.LogInfo("[Heraldry] " + text);
            global::Console.instance?.Print("[Heraldry] " + text);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }

        private static void ShowCentre(string text)
        {
            Jotunn.Logger.LogInfo("[Heraldry] " + text);
            global::Console.instance?.Print("[Heraldry] " + text);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, text);
        }

        internal static string Mb(long bytes) => bytes < 1 << 20 ? $"{Math.Max(1, bytes >> 10)} KB" : $"{bytes / (double)(1 << 20):0.0} MB";
    }
}
