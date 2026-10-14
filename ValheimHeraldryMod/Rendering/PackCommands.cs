using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>heraldry_packs: what this server offers and the state of each pack.</summary>
    internal class PacksCommand : ConsoleCommand
    {
        public override string Name => "heraldry_packs";
        public override string Help => "Heraldry: list the charge packs this server offers (or your own, when you're the server).";

        public override void Run(string[] args)
        {
            if (!PackSync.OnRemoteServer)
            {
                var local = ChargePacks.Instance.LocalBundles;
                Print(local.Count == 0
                    ? $"No charge packs in {ChargePacks.Folder}."
                    : $"Your charge packs (used here, and sent to players if you're the server): " +
                      string.Join(", ", local.Select(b => $"'{b.Name}' v{b.Version} ({b.Charges} charges, {PackSync.Mb(b.Zip.Length)})")));
                return;
            }
            if (PackSync.Offers.Count == 0)
            {
                Print("This server offers no charge packs.");
                return;
            }
            if (!HeraldryConfig.ReceiveServerPacks) Print("ReceiveServerPacks is off in the Heraldry config, so none are downloaded.");
            foreach (var o in PackSync.Offers)
                Print($"'{o.Name}' v{o.Version}: {o.Charges} charges, {PackSync.Mb(o.Size)}, {o.Status}" +
                      (TrustStore.IsTrusted(o.Fingerprint) ? ", allowed" : ""));
        }

        internal static void Print(string text) => global::Console.instance?.Print("[Heraldry] " + text);
    }

    /// <summary>heraldry_trust &lt;pack|all&gt;: allow this server's pack (this exact version) and fetch it.</summary>
    internal class TrustCommand : ConsoleCommand
    {
        public override string Name => "heraldry_trust";
        public override string Help => "Heraldry: allow a charge pack this server offers (heraldry_trust <pack> or heraldry_trust all). Asked again whenever the pack changes.";
        public override List<string> CommandOptionList() => PackSync.Offers.Select(o => o.Name).Concat(new[] { "all" }).ToList();

        public override void Run(string[] args)
        {
            if (args.Length == 0)
            {
                PacksCommand.Print("Usage: heraldry_trust <pack> or heraldry_trust all. See heraldry_packs for this server's packs.");
                return;
            }
            string name = string.Join(" ", args).Trim().ToLowerInvariant();
            var chosen = PackSync.Offers.Where(o => name == "all" || o.Name == name).ToList();
            if (chosen.Count == 0)
            {
                PacksCommand.Print($"This server doesn't offer a pack called '{name}'. See heraldry_packs.");
                return;
            }
            foreach (var o in chosen)
            {
                TrustStore.Trust(o);
                PacksCommand.Print($"Allowed '{o.Name}' v{o.Version}.");
            }
            if (!HeraldryConfig.ReceiveServerPacks)
                PacksCommand.Print("ReceiveServerPacks is off in the Heraldry config; turn it on to download them.");
            PackSync.Fetch();
        }
    }

    /// <summary>heraldry_untrust &lt;pack|all&gt;: forget an allowed pack (it stays in effect until you leave the server).</summary>
    internal class UntrustCommand : ConsoleCommand
    {
        public override string Name => "heraldry_untrust";
        public override string Help => "Heraldry: stop allowing a charge pack (heraldry_untrust <pack> or heraldry_untrust all). Takes effect next time you join.";

        public override void Run(string[] args)
        {
            if (args.Length == 0)
            {
                PacksCommand.Print("Usage: heraldry_untrust <pack> or heraldry_untrust all.");
                return;
            }
            string name = string.Join(" ", args).Trim().ToLowerInvariant();
            int n = TrustStore.UntrustByName(name);
            PacksCommand.Print(n == 0 ? $"No allowed pack called '{name}'." : $"Removed {n} allowed version(s) of '{name}'. Takes effect next time you join a server.");
        }
    }
}
