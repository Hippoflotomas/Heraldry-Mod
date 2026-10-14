using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;
using ValheimHeraldryMod.Shields;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     Painted inventory icons (Hippo, 2026-10-14: "the trick from ShieldShare"): the arms drawn flat inside
    ///     the shield's face outline (a tabard: its front panel), with a dark edge, 128 px. Vanilla icons are
    ///     per item type; these are per item, so <c>ItemData.GetIcon</c> (which the inventory, hotbar and crafting
    ///     screens all use) is patched to return the painted one. Drawn on a worker thread; until it's ready the
    ///     vanilla icon shows.
    /// </summary>
    [HarmonyPatch]
    internal static class ItemIcons
    {
        private const int Size = 128;
        private const int Margin = 6;
        private const int MaxCached = 400;
        private static readonly Color32 Edge = new Color32(48, 34, 22, 255);

        private sealed class Entry
        {
            public Sprite Sprite;
            public Task<Color32[]> Job;
        }

        private static readonly Dictionary<string, Entry> Cache = new Dictionary<string, Entry>();
        private static readonly List<Entry> Pending = new List<Entry>();

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetIcon))]
        [HarmonyPostfix]
        private static void PaintedIcon(ItemDrop.ItemData __instance, ref Sprite __result)
        {
            try
            {
                var sprite = For(__instance);
                if (sprite != null) __result = sprite;
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Painted icon failed: {ex.Message}");
            }
        }

        private static Sprite For(ItemDrop.ItemData item)
        {
            string text = HeraldicShield.BlazonOf(item);
            if (text.Length == 0 || item.m_dropPrefab == null) return null;
            string prefab = item.m_dropPrefab.name;
            bool tabard = Tabards.TabardItem.IsTabard(item);
            if (!tabard && (!HeraldicShield.IsShield(item) || (HeraldryConfig.IsShieldExcluded(prefab) && !TaggedFace.HasTag(item.m_dropPrefab)))) return null;

            text = BlazonLimits.Drawable(BlazonTextureCache.Normalise(text));
            string key = prefab + "|" + text + (text.IndexOf('[') >= 0 ? "|packs" + ChargeLibrary.Generation : "");
            if (Cache.TryGetValue(key, out var entry)) return entry.Sprite;

            // The shape: the shield's face, or the tabard's front panel
            float aspect;
            float[] uv;
            int[] tris;
            FieldRenderer.Arrangement arrangement;
            float cu = -1f, cv = -1f;
            if (tabard)
            {
                var panel = Tabards.TabardMesh.Front;
                if (panel == null) return null;
                aspect = panel.Aspect; uv = panel.OutlineUV; tris = panel.OutlineTriangles;
                arrangement = FieldRenderer.Arrangement.Chest;
            }
            else
            {
                var face = ShieldPatches.FaceForItem(item);
                if (face == null) return null;
                aspect = face.Aspect; uv = face.OutlineUV; tris = face.OutlineTriangles;
                arrangement = FieldRenderer.Arrangement.Compact;
                cu = face.CentreU; cv = face.CentreV;
            }

            if (Cache.Count >= MaxCached) Clear();
            int inner = Size - 2 * Margin;
            int pw = aspect >= 1f ? inner : Mathf.Max(16, Mathf.RoundToInt(inner * aspect));
            int ph = aspect >= 1f ? Mathf.Max(16, Mathf.RoundToInt(inner / aspect)) : inner;
            var blazon = BlazonParser.Parse(text);
            IconArt.EnsureFor(blazon); // game icons are read here, on the main thread, before the worker runs
            entry = new Entry();
            entry.Job = Task.Run(() => Draw(blazon, uv, tris, pw, ph, arrangement, cu, cv));
            Cache[key] = entry;
            Pending.Add(entry);
            return null;
        }

        /// <summary>The arms inside the outline, a dark rim around it, centred in a transparent square (rows bottom first).</summary>
        private static Color32[] Draw(Blazon blazon, float[] uv, int[] tris, int pw, int ph, FieldRenderer.Arrangement arrangement, float cu, float cv)
        {
            var mask = OutlineMask.FromTriangles(uv, tris, pw, ph);
            var arms = FieldRenderer.Render(blazon, pw, ph, mask, arrangement, cu, cv);
            var px = new Color32[Size * Size];
            int ox = (Size - pw) / 2, oy = (Size - ph) / 2;
            bool In(int x, int y) => x >= 0 && y >= 0 && x < pw && y < ph && (mask == null || mask[y * pw + x]); // null = the whole box
            for (int y = 0; y < ph; y++)
                for (int x = 0; x < pw; x++)
                {
                    if (!In(x, y)) continue;
                    bool rim = !In(x - 1, y) || !In(x + 1, y) || !In(x, y - 1) || !In(x, y + 1)
                               || !In(x - 2, y) || !In(x + 2, y) || !In(x, y - 2) || !In(x, y + 2);
                    var c = arms[y * pw + x];
                    if (!rim && c.A < 128) continue; // tattered: a gap
                    int ty = Size - 1 - (oy + y); // the render's rows run top down
                    px[ty * Size + ox + x] = rim ? Edge : new Color32(c.R, c.G, c.B, 255);
                }
            return px;
        }

        /// <summary>Main thread, once per frame: finished icons become sprites.</summary>
        public static void Pump()
        {
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var e = Pending[i];
                if (!e.Job.IsCompleted) continue;
                Pending.RemoveAt(i);
                if (e.Job.IsFaulted)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] Painted icon failed: {e.Job.Exception?.GetBaseException().Message}");
                    continue;
                }
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Heraldry_Icon", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(e.Job.Result);
                tex.Apply(false, true);
                e.Sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
                e.Sprite.name = "Heraldry_Icon";
            }
        }

        private static void Clear()
        {
            foreach (var e in Cache.Values)
            {
                if (e.Sprite == null) continue;
                Object.Destroy(e.Sprite.texture);
                Object.Destroy(e.Sprite);
            }
            Cache.Clear();
            Pending.Clear();
        }
    }
}
