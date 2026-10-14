using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ValheimHeraldryMod
{
    /// <summary>All config entries in one place. Admin-only entries are synced from the server by Jotunn.</summary>
    internal static class HeraldryConfig
    {
        private static ConfigEntry<int> maxBannerResolution;
        private static ConfigEntry<int> bannerResolution;
        private static ConfigEntry<string> excludeGameIcons;
        private static ConfigEntry<int> maxPackMB, maxServedMB, maxDownloadMB;
        private static ConfigEntry<bool> receiveServerPacks;
        private static ConfigEntry<int> maxBlazonLength, maxBlazonElements;
        private static ConfigEntry<string> excludeShields;
        private static ConfigEntry<bool> basicModeDefault;
        private static ConfigEntry<string> lastEditorMode;
        private static ConfigEntry<bool> paintCosts, paintCostsOnBanners;
        private static ConfigEntry<int> paintPerTincture, maxPaintPerPainting, paintRecipeAmount;
        private static readonly Dictionary<Heraldry.Paint, ConfigEntry<string>> paintItems = new Dictionary<Heraldry.Paint, ConfigEntry<string>>();
        private static readonly Dictionary<Heraldry.Paint, ConfigEntry<string>> paintRecipes = new Dictionary<Heraldry.Paint, ConfigEntry<string>>();

        /// <summary>Painting arms costs paint at all (the flat toggle).</summary>
        public static bool PaintCosts => paintCosts?.Value ?? true;
        /// <summary>Editing a banner costs paint too (only when <see cref="PaintCosts"/> is on).</summary>
        public static bool PaintCostsOnBanners => PaintCosts && (paintCostsOnBanners?.Value ?? true);
        public static int PaintPerTincture => paintPerTincture?.Value ?? 4;
        public static int MaxPaintPerPainting => maxPaintPerPainting?.Value ?? 12;
        /// <summary>How many bottles one paint recipe makes.</summary>
        public static int PaintRecipeAmount => paintRecipeAmount?.Value ?? 4;
        /// <summary>The item (prefab name) paid for one unit of this paint.</summary>
        public static string PaintItem(Heraldry.Paint paint) =>
            paintItems.TryGetValue(paint, out var e) ? e.Value.Trim() : Paints.PaintItems.PrefabName(paint);
        /// <summary>What crafting this paint takes, e.g. "Raspberry:1" (comma separated item:amount pairs).</summary>
        public static string PaintRecipe(Heraldry.Paint paint) =>
            paintRecipes.TryGetValue(paint, out var e) ? e.Value : Paints.PaintItems.DefaultRecipe(paint);
        /// <summary>Raised when any paint setting changes (also when the server's values arrive).</summary>
        public static event System.Action PaintSettingsChanged;

        /// <summary>Longest blazon allowed (characters), from the server.</summary>
        public static int MaxBlazonLength => maxBlazonLength?.Value ?? 500;
        /// <summary>Most elements (ordinaries, partitions, charges) a blazon may draw, from the server.</summary>
        public static int MaxBlazonElements => maxBlazonElements?.Value ?? 25;

        /// <summary>The editor mode last used on this computer ("basic" or "advanced").</summary>
        public static string LastEditorMode
        {
            get => lastEditorMode?.Value ?? "basic";
            set { if (lastEditorMode != null && lastEditorMode.Value != value) lastEditorMode.Value = value; }
        }

        /// <summary>True if this shield (by prefab name) is left alone: another mod's own shields, e.g. ShieldShare's.</summary>
        public static bool IsShieldExcluded(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            foreach (var part in (excludeShields?.Value ?? "ShieldShare_").Split(','))
            {
                string p = part.Trim();
                if (p.Length > 0 && prefabName.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Accept charge packs from servers at all (each pack still needs heraldry_trust).</summary>
        public static bool ReceiveServerPacks => receiveServerPacks?.Value ?? false;
        public static long MaxPackBytes => (long)(maxPackMB?.Value ?? 8) << 20;
        public static long MaxServedBytes => (long)(maxServedMB?.Value ?? 32) << 20;
        public static long MaxDownloadBytes => (long)(maxDownloadMB?.Value ?? 64) << 20;

        public static void Bind(ConfigFile config)
        {
            maxBannerResolution = config.Bind("Server", "MaxBannerResolution", 1024,
                new ConfigDescription(
                    "Highest banner texture height (pixels) any client may render. Width follows the banner's shape. " +
                    "Synced from the server; only admins can change it.",
                    new AcceptableValueList<int>(256, 512, 1024, 2048),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            excludeGameIcons = config.Bind("Server", "ExcludeGameIcons", "",
                new ConfigDescription(
                    "Game icons that can't be used as charges, comma separated by prefab name (e.g. 'TrophyEikthyr, TrophyBoar'), " +
                    "or 'all'. Excluded charges show as the placeholder roundel. Synced from the server; only admins can change it.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            maxPackMB = config.Bind("Server", "MaxChargePackMB", 8,
                new ConfigDescription("Largest charge pack (zipped) this server sends to players, in MB. Bigger packs are left out and logged.",
                    new AcceptableValueRange<int>(1, 64),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            maxServedMB = config.Bind("Server", "MaxChargePacksTotalMB", 32,
                new ConfigDescription("Most charge-pack data (zipped, all packs together) this server sends to each player, in MB.",
                    new AcceptableValueRange<int>(1, 256),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            maxBlazonLength = config.Bind("Server", "MaxBlazonLength", 500,
                new ConfigDescription("Longest blazon players may write, in characters. Longer ones are refused by the editor " +
                                      "and drawn as the placeholder roundel. Synced from the server; only admins can change it.",
                    new AcceptableValueRange<int>(50, 4000),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            maxBlazonElements = config.Bind("Server", "MaxBlazonElements", 25,
                new ConfigDescription("Most things one blazon may draw: each ordinary, each partitioned or patterned field and " +
                                      "each charge counts one (three mullets = three). Protects render times. Synced from the server; " +
                                      "only admins can change it.",
                    new AcceptableValueRange<int>(5, 200),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            excludeShields = config.Bind("Server", "ExcludeShields", "ShieldShare_",
                new ConfigDescription("Shields that can't be painted, by prefab name prefix, comma separated (default: ShieldShare's " +
                                      "own shields, which carry their own designs). Synced from the server; only admins can change it.",
                    null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            // ---- paint costs (Hippo, 2026-10-14) ----
            var admin = new ConfigurationManagerAttributes { IsAdminOnly = true };
            paintCosts = config.Bind("Paint", "PaintCosts", true,
                new ConfigDescription("Painting arms costs paint. Off = painting is free. Synced from the server; only admins can change it.",
                    null, admin));
            paintCostsOnBanners = config.Bind("Paint", "PaintCostsOnBanners", true,
                new ConfigDescription("Changing a banner's arms costs paint too (only when PaintCosts is on).", null,
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
            paintPerTincture = config.Bind("Paint", "PaintPerTincture", 4,
                new ConfigDescription("Paint each tincture in the arms costs, split by how it's mixed: gules = 4 red, " +
                                      "vert = 2 yellow + 2 blue, argent = 4 white. Each tincture is paid once.",
                    new AcceptableValueRange<int>(1, 20), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            maxPaintPerPainting = config.Bind("Paint", "MaxPaintPerPainting", 12,
                new ConfigDescription("Most paint one painting can cost in all; busier arms are scaled down to this, keeping the mix.",
                    new AcceptableValueRange<int>(1, 200), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            paintRecipeAmount = config.Bind("Paint", "PaintRecipeAmount", 4,
                new ConfigDescription("Bottles of paint one recipe makes at the herald's table.",
                    new AcceptableValueRange<int>(1, 50), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            foreach (var paint in Heraldry.PaintCost.All)
            {
                paintItems[paint] = config.Bind("Paint", $"{paint}PaintItem", Paints.PaintItems.PrefabName(paint),
                    new ConfigDescription($"The item (prefab name) paid for each unit of {paint.ToString().ToLowerInvariant()} paint. " +
                                          "Default: this mod's own paint bottle; any item works, e.g. 'Raspberry'.",
                        null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
                paintRecipes[paint] = config.Bind("Paint", $"{paint}PaintRecipe", Paints.PaintItems.DefaultRecipe(paint),
                    new ConfigDescription($"What a batch of {paint.ToString().ToLowerInvariant()} paint takes at the herald's table: " +
                                          "item:amount pairs by prefab name, comma separated (e.g. 'Blueberries:1, Dandelion:1').",
                        null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
                paintRecipes[paint].SettingChanged += (_, __) => PaintSettingsChanged?.Invoke();
            }
            paintRecipeAmount.SettingChanged += (_, __) => PaintSettingsChanged?.Invoke();

            lastEditorMode = config.Bind("Client", "EditorMode", "basic",
                new ConfigDescription("Which tab the blazon window opens on: 'basic' (dropdowns) or 'advanced' (write the blazon). " +
                                      "Remembers the last one you used.",
                    new AcceptableValueList<string>("basic", "advanced")));

            receiveServerPacks = config.Bind("Client", "ReceiveServerPacks", false,
                "Accept charge packs (custom charge art) from the servers you join. Off by default. Even when on, each pack " +
                "has to be allowed once with the console command 'heraldry_trust <pack>' (or 'heraldry_trust all'), and again " +
                "whenever the server changes it.");
            maxDownloadMB = config.Bind("Client", "MaxChargePackDownloadMB", 64,
                new ConfigDescription("Most charge-pack data this computer accepts from one server, in MB, whatever the server says.",
                    new AcceptableValueRange<int>(1, 512)));

            bannerResolution = config.Bind("Client", "BannerResolution", 0,
                new ConfigDescription(
                    "Banner texture height on this computer. 0 = use the server's maximum. " +
                    "Lower saves video memory; it is capped at the server's maximum.",
                    new AcceptableValueList<int>(0, 256, 512, 1024, 2048)));
        }

        /// <summary>True if the server's exclude list blocks this icon (key in lower case, e.g. "trophyboar").</summary>
        public static bool IsGameIconExcluded(string key)
        {
            string list = excludeGameIcons?.Value ?? "";
            if (list.Trim().Length == 0) return false;
            foreach (var part in list.Split(','))
            {
                string p = part.Trim().ToLowerInvariant();
                if (p == "all" || p == key) return true;
            }
            return false;
        }

        /// <summary>The height banners render at here: the client's choice, never above the server's maximum.</summary>
        public static int EffectiveBannerResolution()
        {
            int max = maxBannerResolution?.Value ?? 1024;
            int wanted = bannerResolution?.Value ?? 0;
            return wanted <= 0 ? max : Mathf.Min(wanted, max);
        }
    }
}
