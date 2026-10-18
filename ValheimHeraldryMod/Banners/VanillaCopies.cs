using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Managers;
using UnityEngine;

namespace ValheimHeraldryMod.Banners
{
    /// <summary>
    ///     The heraldic versions of vanilla cloth pieces (Hippo, 2026-10-18): which vanilla piece each copies, its recipe
    ///     and station from the config (section Recipes, admin-only, synced; defaults = the vanilla recipes as of the
    ///     rip), and an inventory/hammer icon with this mod's icon in the corner so they're easy to tell apart from the
    ///     plain vanilla pieces.
    /// </summary>
    internal static class VanillaCopies
    {
        internal sealed class Copy
        {
            public string Prefab, Source, Name, Description, DefaultRecipe, DefaultStation;
            public ConfigEntry<string> Recipe, Station;
            /// <summary>Config key: the prefab name without the mod prefix ("BannerHanging").</summary>
            public string Key => Prefab.Replace("ValheimHeraldryMod_", "");
        }

        // Vanilla recipes read from the rip (2026-10-18): banner 2 FineWood, 6 LeatherScraps, 4 Coal; the cloths
        // 4 Jute (red or blue) and 1 FineWood; all at the workbench.
        internal static readonly Copy[] All =
        {
            new Copy { Prefab = "ValheimHeraldryMod_BannerHanging", Source = "piece_banner01", Name = "Heraldic banner (hanging)",
                Description = "A plain cloth banner. Write a blazon on it and every viewer sees the arms painted.",
                DefaultRecipe = "FineWood:2, LeatherScraps:6, Coal:4", DefaultStation = "piece_workbench" },
            new Copy { Prefab = "ValheimHeraldryMod_Curtain", Source = "piece_cloth_hanging_door", Name = "Heraldic curtain",
                Description = "A cloth curtain hung from a beam. Write a blazon on it.",
                DefaultRecipe = "JuteRed:4, FineWood:1", DefaultStation = "piece_workbench" },
            new Copy { Prefab = "ValheimHeraldryMod_CurtainNarrow", Source = "piece_cloth_hanging_door_blue", Name = "Heraldic curtain (narrow)",
                Description = "A long, narrow hanging cloth. Write a blazon on it.",
                DefaultRecipe = "JuteBlue:4, FineWood:1", DefaultStation = "piece_workbench" },
            new Copy { Prefab = "ValheimHeraldryMod_Drape", Source = "piece_cloth_hanging_door_blue2", Name = "Heraldic drape (wide)",
                Description = "A wide cloth drape. Write a blazon on it.",
                DefaultRecipe = "JuteBlue:4, FineWood:1", DefaultStation = "piece_workbench" },
        };

        /// <summary>From HeraldryConfig.Bind, at startup (so the entries exist before the server's values are synced).</summary>
        public static void BindConfig(ConfigFile config)
        {
            foreach (var c in All)
            {
                c.Recipe = config.Bind("Recipes", c.Key + "Recipe", c.DefaultRecipe,
                    new ConfigDescription($"Build cost of the {c.Name.ToLowerInvariant()}: item:amount pairs by prefab name, comma " +
                                          $"separated. Default: the vanilla {c.Source} recipe.",
                        null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
                c.Station = config.Bind("Recipes", c.Key + "Station", c.DefaultStation,
                    new ConfigDescription($"Crafting station needed nearby to build the {c.Name.ToLowerInvariant()} (prefab name, e.g. " +
                                          "piece_workbench, forge, piece_stonecutter, piece_artisanstation; empty = none).",
                        null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
                c.Recipe.SettingChanged += (_, __) => Apply();
                c.Station.SettingChanged += (_, __) => Apply();
            }
        }

        public static void Init()
        {
            SynchronizationManager.OnConfigurationSynchronized += (_, __) => Apply();
            PieceManager.OnPiecesRegistered += Apply;
        }

        /// <summary>The recipe for registering the piece (Jotunn resolves the item names).</summary>
        public static RequirementConfig[] Requirements(Copy c) =>
            Paints.PaintItems.ParseRecipe(c.Recipe?.Value ?? c.DefaultRecipe)
                .Select(kv => new RequirementConfig(kv.Key, kv.Value, 0, true)).ToArray();

        public static string StationOf(Copy c) => (c.Station?.Value ?? c.DefaultStation ?? "").Trim();

        /// <summary>Puts the configured recipes and stations on the registered pieces (server values, admin changes).</summary>
        public static void Apply()
        {
            if (ObjectDB.instance == null || PrefabManager.Instance == null) return;
            foreach (var c in All)
            {
                var piece = PrefabManager.Instance.GetPrefab(c.Prefab)?.GetComponent<Piece>();
                if (piece == null) continue;
                var resources = new List<Piece.Requirement>();
                foreach (var kv in Paints.PaintItems.ParseRecipe(c.Recipe.Value))
                {
                    var item = ObjectDB.instance.GetItemPrefab(kv.Key)?.GetComponent<ItemDrop>();
                    if (item == null)
                    {
                        Jotunn.Logger.LogWarning($"[Heraldry] {c.Key}Recipe: no item '{kv.Key}'; left out.");
                        continue;
                    }
                    resources.Add(new Piece.Requirement { m_resItem = item, m_amount = kv.Value, m_recover = true });
                }
                if (resources.Count > 0) piece.m_resources = resources.ToArray(); // else keep the old recipe rather than build from nothing

                string station = StationOf(c);
                if (station.Length == 0) piece.m_craftingStation = null;
                else
                {
                    var cs = PrefabManager.Instance.GetPrefab(station)?.GetComponent<CraftingStation>();
                    if (cs != null) piece.m_craftingStation = cs;
                    else Jotunn.Logger.LogWarning($"[Heraldry] {c.Key}Station: no crafting station '{station}'; kept the old one.");
                }
            }
        }

        // ================================================================== icon badge

        private const int IconSize = 128;
        private const float BadgeFraction = 0.5f; // of the icon's width
        private static Color32[] badge;
        private static int badgeSize;
        private static bool badgeTried;

        /// <summary>
        ///     The vanilla piece's icon with this mod's icon (the arms on a heater shield) in the bottom right corner, or
        ///     the vanilla icon unchanged if anything can't be read (and on a dedicated server, which draws nothing).
        /// </summary>
        public static Sprite Badged(Sprite vanilla, string name)
        {
            if (vanilla == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return vanilla;
            try
            {
                var px = Rendering.IconArt.ReadRegion(vanilla.texture, Rendering.IconArt.SourceRect(vanilla), IconSize, IconSize, true);
                var b = Badge(Mathf.RoundToInt(IconSize * BadgeFraction));
                if (b == null) return vanilla;
                int s = badgeSize, ox = IconSize - s, oy = 0; // bottom right (rows bottom to top)
                for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    var top = b[y * s + x];
                    if (top.a == 0) continue;
                    int i = (oy + y) * IconSize + ox + x;
                    var under = px[i];
                    float a = top.a / 255f;
                    px[i] = new Color32((byte)(top.r * a + under.r * (1 - a)), (byte)(top.g * a + under.g * (1 - a)),
                        (byte)(top.b * a + under.b * (1 - a)), (byte)Mathf.Max(top.a, under.a));
                }
                var tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false) { name = "Heraldry_Icon_" + name, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(px);
                tex.Apply(false, true);
                Object.DontDestroyOnLoad(tex);
                var sprite = Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = "Heraldry_Icon_" + name;
                return sprite;
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Couldn't badge the icon of '{name}' ({ex.Message}); using the vanilla icon.");
                return vanilla;
            }
        }

        /// <summary>The mod's icon (embedded Package/icon.png), scaled down to size x size, rows bottom to top; null if missing.</summary>
        private static Color32[] Badge(int size)
        {
            if (badgeTried) return badge != null && badgeSize == size ? badge : null;
            badgeTried = true;
            var assembly = typeof(VanillaCopies).Assembly;
            string res = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("ModIcon.png"));
            if (res == null) { Jotunn.Logger.LogWarning("[Heraldry] The mod icon is missing from the dll; icons stay vanilla."); return null; }
            byte[] bytes;
            using (var stream = assembly.GetManifestResourceStream(res))
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                bytes = memory.ToArray();
            }
            var src = Heraldry.MiniPng.Decode(bytes, 1024, out int w, out int h); // rows top to bottom

            // Box-filter down to size x size (premultiplied, so transparent edges don't darken), flipped to bottom-up
            var dst = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int x0 = x * w / size, x1 = Mathf.Max(x0 + 1, (x + 1) * w / size);
                int y0 = y * h / size, y1 = Mathf.Max(y0 + 1, (y + 1) * h / size);
                float r = 0, g = 0, b = 0, a = 0; int n = 0;
                for (int sy = y0; sy < y1; sy++)
                for (int sx = x0; sx < x1; sx++)
                {
                    var p = src[sy * w + sx];
                    float pa = p.A / 255f;
                    r += p.R * pa; g += p.G * pa; b += p.B * pa; a += pa; n++;
                }
                var c = a > 0 ? new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / n * 255f)) : new Color32(0, 0, 0, 0);
                dst[(size - 1 - y) * size + x] = c;
            }
            badge = dst;
            badgeSize = size;
            return badge;
        }
    }
}
