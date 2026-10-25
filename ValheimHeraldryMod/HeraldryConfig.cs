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
        // Charges between players (2026-10-14 rework)
        private static ConfigEntry<string> chargeRoot, serverChargeFolder;
        private static ConfigEntry<bool> autoGetServerCharges, shareCharges, ownChargesFirst;
        private static ConfigEntry<int> maxChargeKB, maxDownloadMB, maxUploadMB, serverMaxChargeKB, serverMaxUploadMB, relayCacheMB;
        private static ConfigEntry<int> maxBlazonLength, maxBlazonElements;
        private static ConfigEntry<string> excludeShields;
        private static ConfigEntry<string> lastEditorMode;
        private static ConfigEntry<KeyCode> editModifier, editKey;
        private static ConfigEntry<bool> allowSecretBlazons, showMottos;
        private static ConfigEntry<int> maxInscriptionLength;

        /// <summary>The secret codes work on this server.</summary>
        public static bool AllowSecretBlazons => allowSecretBlazons?.Value ?? true;
        /// <summary>Players see the words on scrolls and ordinaries (admins always do).</summary>
        public static bool ShowMottos => showMottos?.Value ?? true;
        public static int MaxInscriptionLength => maxInscriptionLength?.Value ?? 40;

        /// <summary>The key (with <see cref="EditModifier"/>) that edits arms on a tagged piece that already uses E.</summary>
        public static KeyCode EditKey => editKey?.Value ?? KeyCode.E;
        public static KeyCode EditModifier => editModifier?.Value ?? KeyCode.LeftShift;
        /// <summary>The default Shift+E: the game's own alternative use, handled through its interact.</summary>
        public static bool EditKeyIsAltUse => EditKey == KeyCode.E && EditModifier == KeyCode.LeftShift;
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
        private static ConfigEntry<bool> compendiumPages;
        public static bool CompendiumPages => compendiumPages?.Value ?? true;

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
        /// <summary>Folder for your charges, downloads and block list; empty = Documents\ValheimHeraldryMod.</summary>
        public static string ChargeRoot => chargeRoot?.Value ?? "";
        /// <summary>The charges this computer serves when it's the server; empty = ...\ServerCharges.</summary>
        public static string ServerChargeFolder => serverChargeFolder?.Value ?? "";
        public static bool AutoGetServerCharges => autoGetServerCharges?.Value ?? false;
        public static bool ShareCharges => shareCharges?.Value ?? true;
        public static bool OwnChargesFirst => ownChargesFirst?.Value ?? false;
        /// <summary>Largest charge this computer accepts (client).</summary>
        public static long MaxChargeBytes => (long)(maxChargeKB?.Value ?? 1024) << 10;
        public static long MaxDownloadBytes => (long)(maxDownloadMB?.Value ?? 64) << 20;
        public static long MaxUploadBytes => (long)(maxUploadMB?.Value ?? 32) << 20;
        /// <summary>Largest charge the server passes on.</summary>
        public static long ServerMaxChargeBytes => (long)(serverMaxChargeKB?.Value ?? 1024) << 10;
        public static long ServerMaxUploadBytes => (long)(serverMaxUploadMB?.Value ?? 16) << 20;
        public static long RelayCacheBytes => (long)(relayCacheMB?.Value ?? 32) << 20;

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

            serverMaxChargeKB = config.Bind("Server", "MaxChargeKB", 1024,
                new ConfigDescription("Largest charge picture (KB) this server passes between players.",
                    new AcceptableValueRange<int>(16, 8192), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            serverMaxUploadMB = config.Bind("Server", "MaxChargeUploadMBPerPlayer", 16,
                new ConfigDescription("Most charge-picture data (MB) the server takes from one player per session when passing charges on.",
                    new AcceptableValueRange<int>(1, 1024), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            relayCacheMB = config.Bind("Server", "RelayCacheMB", 32,
                new ConfigDescription("Memory (MB) the server uses to remember charges it passed on, so it doesn't fetch them twice. " +
                                      "Kept until the server restarts, never written to disk.",
                    new AcceptableValueRange<int>(0, 1024), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            serverChargeFolder = config.Bind("Server", "ServerChargeFolder", "",
                "Folder of charges this computer offers to everyone when it's the server (hosting or dedicated). " +
                "Empty = Documents\\ValheimHeraldryMod\\ServerCharges. Pack folders, .zip packs or loose PNGs.");

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

            // Build costs of the heraldic copies of vanilla cloth pieces (2026-10-18)
            Banners.VanillaCopies.BindConfig(config);

            allowSecretBlazons = config.Bind("Server", "AllowSecretBlazons", true,
                new ConfigDescription("A few secret words stand for special designs. Off = they're just text that isn't blazon.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            showMottos = config.Bind("Server", "ShowMottos", true,
                new ConfigDescription("Show the words written on scrolls and ordinaries (inscribed \"...\"). Off = players see them blank; " +
                                      "admins still see what was written.", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            maxInscriptionLength = config.Bind("Server", "MaxInscriptionLength", 40,
                new ConfigDescription("Longest inscription (motto), in characters.", new AcceptableValueRange<int>(1, 200),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            editModifier = config.Bind("Client", "TaggedPieceEditModifier", KeyCode.LeftShift,
                "Hold this (None = nothing) and press TaggedPieceEditKey to edit the arms on another mod's piece that already " +
                "uses E for something else (a chest, a door). Default Shift+E; change it if that mod uses Shift+E too.");
            editKey = config.Bind("Client", "TaggedPieceEditKey", KeyCode.E,
                "See TaggedPieceEditModifier. Pieces with nothing else to do are edited with E as usual.");

            compendiumPages = config.Bind("Client", "CompendiumPages", true,
                "Show the heraldry guide pages (how to write a blazon, colours, ordinaries, charges...) in the compendium.");
            lastEditorMode = config.Bind("Client", "EditorMode", "basic",
                new ConfigDescription("Which tab the blazon window opens on: 'basic' (dropdowns) or 'advanced' (write the blazon). " +
                                      "Remembers the last one you used.",
                    new AcceptableValueList<string>("basic", "advanced")));

            chargeRoot = config.Bind("Client", "ChargeFolder", "",
                "Where your charges (Charges), downloaded charges (Downloaded) and block list live. " +
                "Empty = Documents\\ValheimHeraldryMod.");
            autoGetServerCharges = config.Bind("Client", "AutoGetServerCharges", false,
                "Fetch the server's own charges without asking. Other players' charges always wait for 'heraldry_get'.");
            shareCharges = config.Bind("Client", "ShareCharges", true,
                "Send your charge pictures (your own and downloaded ones) when other players on the server need them for arms they see.");
            ownChargesFirst = config.Bind("Client", "OwnChargesFirst", false,
                "When you and the server both have a charge of the same name, use yours when you write it. Off = the server's.");
            maxChargeKB = config.Bind("Client", "MaxChargeKB", 1024,
                new ConfigDescription("Largest charge picture (KB) this computer accepts.", new AcceptableValueRange<int>(16, 8192)));
            maxDownloadMB = config.Bind("Client", "MaxChargeDownloadMB", 64,
                new ConfigDescription("Most charge-picture data (MB) this computer downloads per session.", new AcceptableValueRange<int>(1, 1024)));
            maxUploadMB = config.Bind("Client", "MaxChargeUploadMB", 32,
                new ConfigDescription("Most charge-picture data (MB) this computer sends to others per session.", new AcceptableValueRange<int>(0, 1024)));

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
