using BepInEx.Configuration;
using UnityEngine;

namespace ValheimHeraldryMod
{
    /// <summary>All config entries in one place. Admin-only entries are synced from the server by Jotunn.</summary>
    internal static class HeraldryConfig
    {
        private static ConfigEntry<int> maxBannerResolution;
        private static ConfigEntry<int> bannerResolution;

        public static void Bind(ConfigFile config)
        {
            maxBannerResolution = config.Bind("Server", "MaxBannerResolution", 1024,
                new ConfigDescription(
                    "Highest banner texture height (pixels) any client may render. Width follows the banner's shape. " +
                    "Synced from the server; only admins can change it.",
                    new AcceptableValueList<int>(256, 512, 1024, 2048),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            bannerResolution = config.Bind("Client", "BannerResolution", 0,
                new ConfigDescription(
                    "Banner texture height on this computer. 0 = use the server's maximum. " +
                    "Lower saves video memory; it is capped at the server's maximum.",
                    new AcceptableValueList<int>(0, 256, 512, 1024, 2048)));
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
