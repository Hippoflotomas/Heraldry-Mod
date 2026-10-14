using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     Game icons as charge art. Icons live in a GPU-only atlas, so each one is copied out once through a
    ///     RenderTexture (the readback the probe proved) and handed to <see cref="ChargeArtLibrary"/>.
    ///     Main thread only. Art comes from the player's own game files; nothing is shipped or sent.
    /// </summary>
    internal sealed class IconArt : IGameItems
    {
        private static Dictionary<string, GameObject> itemsByKey;   // lower-case prefab name -> prefab
        private static Dictionary<string, string> keyByShownName;   // folded in-game name ("bronze axe") -> prefab key
        private static readonly HashSet<string> Missing = new HashSet<string>();
        /// <summary>"item:wood" keys (quoted before the item list existed) -> prefab key; read from workers.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ItemKeys =
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        public static readonly IconArt Instance = new IconArt();

        // ------------------------------------------------------------------ IGameItems (for the parser)

        public bool Ready => FindItems();

        public string Find(string name)
        {
            if (!FindItems()) return null;
            if (keyByShownName.TryGetValue(name, out var key)) return key;
            return itemsByKey.ContainsKey(name) ? name : null; // the internal name works too
        }

        public string Suggest(string name)
        {
            if (!FindItems()) return null;
            string best = null;
            int bestD = Math.Max(1, name.Length / 4) + 1;
            foreach (var shown in keyByShownName.Keys)
            {
                if (Math.Abs(shown.Length - name.Length) >= bestD) continue;
                int d = Levenshtein(name, shown);
                if (d < bestD) { bestD = d; best = shown; }
            }
            return best;
        }

        /// <summary>For the art library: is this key blocked by the server's exclude list?</summary>
        public static bool IsBlocked(string key)
        {
            if (key != null && key.StartsWith("pack:")) return false; // the exclude list is for game icons only
            if (key != null && key.StartsWith("item:") && ItemKeys.TryGetValue(key, out var prefabKey)) key = prefabKey;
            return HeraldryConfig.IsGameIconExcluded(key);
        }

        /// <summary>Makes sure every icon this blazon uses is loaded (or known to be unavailable) before rendering.</summary>
        public static void EnsureFor(Blazon blazon)
        {
            if (blazon == null) return;
            foreach (var c in blazon.Charges) Ensure(c);
            foreach (var o in blazon.Ordinaries)
                foreach (var c in o.Charges) Ensure(c);
        }

        private static void Ensure(Charge c)
        {
            string key = c.ArtKey;
            if (key == null || IsBlocked(key)) return;
            if (ChargeArtLibrary.Has(key) || Missing.Contains(key)) return;

            if (!FindItems()) return; // not in a world yet; try again on the next render
            string prefabKey = key;
            if (key.StartsWith("item:"))
            {
                prefabKey = Instance.Find(key.Substring(5)) ?? key;
                ItemKeys[key] = prefabKey;
                if (IsBlocked(key)) return;
            }
            if (!itemsByKey.TryGetValue(prefabKey, out var prefab))
            {
                Missing.Add(key);
                Jotunn.Logger.LogWarning($"[Heraldry] No item '{key}' in this game; that charge shows as the placeholder roundel.");
                return;
            }
            var icons = prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_icons;
            if (icons == null || icons.Length == 0 || icons[0] == null)
            {
                Missing.Add(key);
                Jotunn.Logger.LogWarning($"[Heraldry] '{prefab.name}' has no icon; that charge shows as the placeholder roundel.");
                return;
            }

            try
            {
                var sprite = icons[0];
                Rect r = SourceRect(sprite);
                int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
                var pixels = ReadRegion(sprite.texture, r, w, h, srgb: true);
                // Unity's rows run bottom to top; the renderer's run top to bottom
                var px = new Rgba[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = pixels[(h - 1 - y) * w + x];
                    px[y * w + x] = new Rgba(p.r, p.g, p.b, p.a);
                }
                ChargeArtLibrary.Register(key, px, w, h, deriveLines: true);
            }
            catch (Exception ex)
            {
                Missing.Add(key);
                Jotunn.Logger.LogWarning($"[Heraldry] Couldn't read the icon of '{prefab.name}': {ex.Message}");
            }
        }

        private static bool FindItems()
        {
            if (itemsByKey != null) return true;
            var db = ObjectDB.instance;
            if (db == null || db.m_items == null || db.m_items.Count == 0) return false;
            itemsByKey = new Dictionary<string, GameObject>();
            keyByShownName = new Dictionary<string, string>();
            foreach (var item in db.m_items)
            {
                if (item == null) continue;
                string key = item.name.ToLowerInvariant();
                itemsByKey[key] = item;
                // The name players see, in their language ("$item_wood" -> "Wood"); the first item with a name keeps it
                string token = item.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name;
                if (string.IsNullOrEmpty(token) || Localization.instance == null) continue;
                string shown = Fold(Localization.instance.Localize(token));
                if (shown.Length > 0 && !shown.StartsWith("$") && !keyByShownName.ContainsKey(shown))
                    keyByShownName[shown] = key;
            }
            return true;
        }

        /// <summary>Same folding as the parser: lower case, accents off, single spaces.</summary>
        private static string Fold(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD))
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return string.Join(" ", sb.ToString().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static int Levenshtein(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        /// <summary>Pixel rectangle of the sprite inside its texture. Tight-packed sprites have no valid textureRect.</summary>
        internal static Rect SourceRect(Sprite sprite)
        {
            if (sprite.packed && sprite.packingMode == SpritePackingMode.Tight)
                throw new NotSupportedException("tight-packed sprite: textureRect is not available");
            return sprite.packed ? sprite.textureRect : sprite.rect;
        }

        /// <summary>
        ///     GPU copy of a region of any texture (compressed and non-readable included) into a pixel array
        ///     (rows bottom to top). Same idea as ShieldShare's ReadTexturePixels, plus a source rectangle and an
        ///     sRGB target for colour images.
        /// </summary>
        internal static Color32[] ReadRegion(Texture source, Rect region, int width, int height, bool srgb)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                var scale = new Vector2(region.width / source.width, region.height / source.height);
                var offset = new Vector2(region.x / source.width, region.y / source.height);
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(0, 0, 0, 0));
                Graphics.Blit(source, rt, scale, offset);
                RenderTexture.active = rt;
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false, !srgb);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply();
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null)
                    UnityEngine.Object.Destroy(copy);
            }
        }
    }
}
