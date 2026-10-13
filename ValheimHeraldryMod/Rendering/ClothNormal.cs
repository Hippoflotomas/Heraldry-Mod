using System.IO;
using System.Linq;
using Jotunn.Utils;
using UnityEngine;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     The plain twill-weave normal map from BannerShare (Art/Banner Bump Map.png, embedded in the dll).
    ///     Replaces the vanilla banner's normal map, whose emblem relief otherwise shows through our arms.
    ///     Loaded once and shared by every banner.
    /// </summary>
    internal static class ClothNormal
    {
        private const string ResourceSuffix = ".ClothNormal.png";
        private static Texture2D texture;
        private static bool tried;

        public static Texture2D Get()
        {
            if (tried)
                return texture;
            tried = true;

            var assembly = typeof(ClothNormal).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(ResourceSuffix));
            if (name == null)
            {
                Jotunn.Logger.LogWarning("[Heraldry] Cloth normal map is missing from the dll - banners will look flat.");
                return null;
            }

            byte[] bytes;
            using (var stream = assembly.GetManifestResourceStream(name))
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                bytes = memory.ToArray();
            }

            // linear: true. A normal map holds directions, not colours; read as sRGB in Valheim's Linear
            // colour space its flat value (128) would decode to about 0.22 and tilt every normal.
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
            if (!AssetUtils.LoadImage(tex, bytes))
            {
                Object.Destroy(tex);
                Jotunn.Logger.LogWarning("[Heraldry] Cloth normal map could not be decoded - banners will look flat.");
                return null;
            }
            tex.name = "Heraldry_ClothNormal";
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.anisoLevel = 4;
            Object.DontDestroyOnLoad(tex);
            texture = tex;
            return texture;
        }
    }
}
