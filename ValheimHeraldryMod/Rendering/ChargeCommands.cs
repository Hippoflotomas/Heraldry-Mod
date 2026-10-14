using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     The heraldry console commands (2026-10-13 set of ten, built 2026-10-14). Commands about one charge take
    ///     its name ("wolf", "pack:wolf") or fingerprint, or nothing to mean the charges of whatever you're looking
    ///     at (a banner, the herald's table, a shield on a stand or on the ground, another player), or else your
    ///     own shield and tabard.
    /// </summary>
    internal static class ChargeCommands
    {
        public static void Register()
        {
            var cm = CommandManager.Instance;
            cm.AddConsoleCommand(new GetCommand());
            cm.AddConsoleCommand(new PendingCommand());
            cm.AddConsoleCommand(new RemoveCommand());
            cm.AddConsoleCommand(new BlockCommand());
            cm.AddConsoleCommand(new UnblockCommand());
            cm.AddConsoleCommand(new BlockedCommand());
            cm.AddConsoleCommand(new InfoCommand());
            cm.AddConsoleCommand(new BlacklistCommand());
            cm.AddConsoleCommand(new UnblacklistCommand());
            cm.AddConsoleCommand(new BlacklistedCommand());
            cm.AddConsoleCommand(new ReloadCommand());
        }

        internal static void Print(string text) => global::Console.instance?.Print("[Heraldry] " + text);

        internal static string Size(long bytes) => bytes <= 0 ? "size unknown" : ChargeSync.Mb(bytes);

        // ------------------------------------------------------------------ what a command is about

        /// <summary>A charge a command is about: its fingerprint and the best name we have.</summary>
        internal struct Target
        {
            public string Fingerprint, Name;
        }

        /// <summary>
        ///     The charges an argument means: a fingerprint; a name among the charges of what you're looking at;
        ///     a name this computer knows; with no argument, every picture charge of what you're looking at.
        /// </summary>
        internal static List<Target> Targets(string arg, out string what)
        {
            var lib = ChargeLibrary.Instance;
            var result = new List<Target>();
            string text = LookedAt(out what);
            var here = new List<Target>();
            if (text.Length > 0)
            {
                var b = BlazonParser.Parse(text);
                foreach (var c in b.Charges.Concat(b.Ordinaries.SelectMany(o => o.Charges)))
                    if (c.Hash != null && here.All(t => t.Fingerprint != c.Hash))
                        here.Add(new Target { Fingerprint = c.Hash, Name = c.PackName });
            }
            if (string.IsNullOrWhiteSpace(arg)) return here;

            string q = ChargeIndex.Fold(arg).Trim('[', ']');
            if (BlazonText.IsFingerprint(q))
            {
                var e = lib.Index.Get(q);
                string name = e?.Names.FirstOrDefault() ?? here.FirstOrDefault(t => t.Fingerprint == q).Name
                              ?? lib.Pending.FirstOrDefault(p => p.Fingerprint == q)?.Name ?? q;
                result.Add(new Target { Fingerprint = q, Name = name });
                return result;
            }
            result.AddRange(here.Where(t => t.Name != null && (ChargeIndex.Fold(t.Name) == q || ChargeIndex.Fold(t.Name).EndsWith(":" + q))));
            if (result.Count > 0) return result;
            foreach (var e in lib.Index.Find(q)) result.Add(new Target { Fingerprint = e.Fingerprint, Name = e.Names.FirstOrDefault() });
            foreach (var p in lib.Pending.Where(p => p.Name != null && ChargeIndex.Fold(p.Name) == q))
                if (result.All(t => t.Fingerprint != p.Fingerprint)) result.Add(new Target { Fingerprint = p.Fingerprint, Name = p.Name });
            return result;
        }

        /// <summary>The stored blazon of what you're looking at (or your own shield/tabard), "" if none.</summary>
        internal static string LookedAt(out string what)
        {
            what = "nothing";
            var player = Player.m_localPlayer;
            if (player == null) return "";
            var hover = player.GetHoverObject();
            if (hover != null)
            {
                var banner = hover.GetComponentInParent<Banners.HeraldicBanner>();
                if (banner != null) { what = "this banner"; return banner.GetText() ?? ""; }
                var table = hover.GetComponentInParent<Station.HeraldTable>();
                if (table != null) { what = "the herald's table"; return table.GetDisplayText(); }
                var stand = hover.GetComponentInParent<ItemStand>();
                var standView = stand != null ? stand.GetComponent<ZNetView>() : null;
                if (standView != null && standView.IsValid())
                {
                    what = "the shield on this stand";
                    return standView.GetZDO().GetString(Shields.ShieldPatches.ZdoItem, "");
                }
                var drop = hover.GetComponentInParent<ItemDrop>();
                if (drop != null) { what = "this " + Localization.instance.Localize(drop.m_itemData.m_shared.m_name); return Shields.HeraldicShield.BlazonOf(drop.m_itemData); }
            }
            var other = player.GetHoverCreature() as Player;
            if (other != null && other != player)
            {
                var zdo = other.GetComponent<ZNetView>()?.GetZDO();
                if (zdo != null)
                {
                    what = other.GetPlayerName() + "'s arms";
                    string s = zdo.GetString(Shields.ShieldPatches.ZdoLeft, "");
                    if (s.Length == 0) s = zdo.GetString(Shields.ShieldPatches.ZdoBack, "");
                    if (s.Length == 0)
                    {
                        string t = zdo.GetString(Tabards.HeraldicTabardWearer.ZdoKey, "");
                        int bar = t.IndexOf('|');
                        s = bar >= 0 ? t.Substring(bar + 1) : "";
                    }
                    return s;
                }
            }
            foreach (var item in player.GetInventory().GetEquippedItems())
            {
                string s = Shields.HeraldicShield.BlazonOf(item);
                if (s.Length == 0) continue;
                what = "your " + Localization.instance.Localize(item.m_shared.m_name);
                return s;
            }
            return "";
        }

        internal static bool NeedTargets(List<Target> targets, string arg, string what, string verb)
        {
            if (targets.Count > 0) return true;
            Print(string.IsNullOrWhiteSpace(arg)
                ? $"Look at a banner, shield or player with picture charges (now: {what}), or name the charge: {verb} <name>."
                : $"No charge called '{arg}' here or among the charges you know.");
            return false;
        }

        // ------------------------------------------------------------------ the commands

        private class GetCommand : ConsoleCommand
        {
            public override string Name => "heraldry_get";
            public override string Help => "Heraldry: fetch a charge picture you haven't got: heraldry_get <name>, or 'all' for everything waiting.";

            public override void Run(string[] args)
            {
                var lib = ChargeLibrary.Instance;
                string arg = string.Join(" ", args).Trim();
                List<string> fps;
                if (arg.ToLowerInvariant() == "all") fps = lib.Pending.Where(p => !lib.Has(p.Fingerprint)).Select(p => p.Fingerprint).ToList();
                else
                {
                    var targets = Targets(arg, out string what);
                    if (!NeedTargets(targets, arg, what, "heraldry_get")) return;
                    fps = targets.Where(t => !lib.Has(t.Fingerprint)).Select(t => t.Fingerprint).ToList();
                    foreach (var t in targets.Where(t => lib.IsBlocked(t.Fingerprint)))
                        Print($"[{t.Name}] is on your block list; 'heraldry_unblock {t.Name}' first.");
                    fps = fps.Where(fp => !lib.IsBlocked(fp)).ToList();
                }
                if (fps.Count == 0) { Print("Nothing to fetch: you have them already (or nothing is waiting)."); return; }
                int n = ChargeSync.Request(fps);
                Print(n > 0 ? $"Asking for {n} charge picture(s)..." : "Already being fetched, or not in a game.");
            }
        }

        private class PendingCommand : ConsoleCommand
        {
            public override string Name => "heraldry_pending";
            public override string Help => "Heraldry: list the charge pictures you've met but haven't got.";

            public override void Run(string[] args)
            {
                var lib = ChargeLibrary.Instance;
                var waiting = lib.Pending.Where(p => !lib.Has(p.Fingerprint)).ToList();
                if (waiting.Count == 0) { Print("No charge pictures waiting."); return; }
                foreach (var w in waiting)
                    Print($"[{w.Name}] {w.Fingerprint}: {(w.FromServer ? "the server's" : "another player's")}, {Size(w.Size)}, {w.Status}");
                Print("'heraldry_get <name>' fetches one, 'heraldry_get all' all of them; 'heraldry_block <name>' hides one for good.");
            }
        }

        private class RemoveCommand : ConsoleCommand
        {
            public override string Name => "heraldry_remove";
            public override string Help => "Heraldry: delete a downloaded charge picture (never your own files): heraldry_remove <name>.";

            public override void Run(string[] args)
            {
                string arg = string.Join(" ", args).Trim();
                var targets = Targets(arg, out string what);
                if (!NeedTargets(targets, arg, what, "heraldry_remove")) return;
                foreach (var t in targets)
                    Print(ChargeLibrary.Instance.RemoveDownloaded(t.Fingerprint)
                        ? $"Deleted the downloaded [{t.Name}] ({t.Fingerprint})."
                        : $"[{t.Name}] ({t.Fingerprint}) isn't a downloaded charge; your own files are left alone.");
            }
        }

        private class BlockCommand : ConsoleCommand
        {
            public override string Name => "heraldry_block";
            public override string Help => "Heraldry: never show a charge picture (you only): heraldry_block <name>, or look at it.";

            public override void Run(string[] args)
            {
                string arg = string.Join(" ", args).Trim();
                var targets = Targets(arg, out string what);
                if (!NeedTargets(targets, arg, what, "heraldry_block")) return;
                foreach (var t in targets)
                {
                    ChargeLibrary.Instance.Block(t.Fingerprint, t.Name);
                    Print($"Blocked [{t.Name}] ({t.Fingerprint}): it shows as the placeholder roundel for you.");
                }
            }
        }

        private class UnblockCommand : ConsoleCommand
        {
            public override string Name => "heraldry_unblock";
            public override string Help => "Heraldry: show a blocked charge again: heraldry_unblock <name>.";

            public override void Run(string[] args)
            {
                var lib = ChargeLibrary.Instance;
                string arg = ChargeIndex.Fold(string.Join(" ", args)).Trim('[', ']');
                var fps = lib.Blocked.Where(kv => kv.Key == arg || ChargeIndex.Fold(kv.Value) == arg).Select(kv => kv.Key).ToList();
                if (fps.Count == 0) fps = Targets(arg, out _).Select(t => t.Fingerprint).Where(lib.IsBlocked).ToList();
                if (fps.Count == 0) { Print($"'{arg}' isn't blocked. 'heraldry_blocked' lists what is."); return; }
                foreach (var fp in fps) { lib.Unblock(fp); Print($"Unblocked {fp}."); }
            }
        }

        private class BlockedCommand : ConsoleCommand
        {
            public override string Name => "heraldry_blocked";
            public override string Help => "Heraldry: list the charge pictures you've blocked.";

            public override void Run(string[] args)
            {
                var list = ChargeLibrary.Instance.Blocked;
                if (list.Count == 0) { Print("You haven't blocked any charges."); return; }
                foreach (var kv in list) Print($"[{kv.Value}] {kv.Key}");
            }
        }

        private class InfoCommand : ConsoleCommand
        {
            public override string Name => "heraldry_info";
            public override string Help => "Heraldry: the arms of what you're looking at, with each picture charge's fingerprint and state.";

            public override void Run(string[] args)
            {
                var lib = ChargeLibrary.Instance;
                string text = LookedAt(out string what);
                if (text.Length == 0) { Print($"No arms on {what}."); return; }
                Print($"{what}: {TextArt.ForDisplay(BlazonText.Visible(text))}");
                var b = BlazonParser.Parse(text);
                var charges = b.Charges.Concat(b.Ordinaries.SelectMany(o => o.Charges)).Where(c => c.PackName != null).ToList();
                if (charges.Count == 0) { Print("No picture charges."); return; }
                foreach (var c in charges)
                {
                    if (c.Hash == null) { Print($"[{c.PackName}]: not known here (no fingerprint)"); continue; }
                    var e = lib.Index.Get(c.Hash);
                    string source = e == null ? "another player's" : e.Source == ChargeSource.Server ? "the server's"
                                  : e.Source == ChargeSource.Own ? "yours" : "downloaded";
                    string state = lib.IsBlacklisted(c.Hash) ? "blacklisted on this server" : lib.IsBlocked(c.Hash) ? "blocked by you"
                                 : lib.Has(c.Hash) ? "shown" : "not here yet (heraldry_get " + c.PackName + ")";
                    Print($"[{c.PackName}] {c.Hash}: {source}{(e?.Pack != null && e.Pack.Length > 0 ? ", pack '" + e.Pack + "'" : "")}, {state}");
                }
            }
        }

        private static bool CheckAdmin()
        {
            if (ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost()) return true;
            Print("Only admins can do that.");
            return false;
        }

        private class BlacklistCommand : ConsoleCommand
        {
            public override string Name => "heraldry_blacklist";
            public override string Help => "Heraldry (admin): ban a charge picture on this server for everyone: heraldry_blacklist <name>, or look at it.";

            public override void Run(string[] args)
            {
                if (!CheckAdmin()) return;
                string arg = string.Join(" ", args).Trim();
                var targets = Targets(arg, out string what);
                if (!NeedTargets(targets, arg, what, "heraldry_blacklist")) return;
                foreach (var t in targets) ChargeSync.Admin(ChargeSync.OpBlacklist, t.Fingerprint, t.Name);
            }
        }

        private class UnblacklistCommand : ConsoleCommand
        {
            public override string Name => "heraldry_unblacklist";
            public override string Help => "Heraldry (admin): allow a blacklisted charge picture again: heraldry_unblacklist <name>.";

            public override void Run(string[] args)
            {
                if (!CheckAdmin()) return;
                string arg = ChargeIndex.Fold(string.Join(" ", args)).Trim('[', ']');
                var lib = ChargeLibrary.Instance;
                var hits = lib.Blacklist.Where(kv => kv.Key == arg || ChargeIndex.Fold(kv.Value) == arg).ToList();
                if (hits.Count == 0) { Print($"'{arg}' isn't on the blacklist. 'heraldry_blacklisted' lists it."); return; }
                foreach (var kv in hits) ChargeSync.Admin(ChargeSync.OpUnblacklist, kv.Key, kv.Value);
            }
        }

        private class BlacklistedCommand : ConsoleCommand
        {
            public override string Name => "heraldry_blacklisted";
            public override string Help => "Heraldry (admin): list the charge pictures blacklisted on this server.";

            public override void Run(string[] args)
            {
                if (!CheckAdmin()) return;
                ChargeSync.Admin(ChargeSync.OpList, "", "");
            }
        }

        private class ReloadCommand : ConsoleCommand
        {
            public override string Name => "heraldry_reload";
            public override string Help => "Heraldry: re-read your charge folder; for admins also the server's charge folder.";

            public override void Run(string[] args)
            {
                ChargeLibrary.Instance.LoadOwn();
                Print($"Your charges reloaded from {ChargeLibrary.OwnFolder}.");
                if (ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost())
                    ChargeSync.Admin(ChargeSync.OpReload, "", "");
            }
        }
    }
}
