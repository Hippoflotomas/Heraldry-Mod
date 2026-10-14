using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;
using ValheimHeraldryMod.Rendering;

namespace ValheimHeraldryMod.Tabards
{
    /// <summary>
    ///     One tabard texture (and material) per blazon, shared by everyone wearing those arms, destroyed when the
    ///     last lets go. Front panel: the full arms, charges centred on the chest. Back panel: the same field and
    ///     ordinaries with no charges (Hippo, 2026-10-14). Drawn on a worker thread; until it's done the tabard
    ///     shows plain linen.
    /// </summary>
    internal static class TabardTextures
    {
        private const int Size = 1024;
        private static readonly Color32 Linen = new Color32(214, 204, 178, 255);

        public sealed class Entry
        {
            public string Key;
            public Material Material;
            public int Users;
            public bool Ready;
            internal Task<Color32[]> Job;
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        private static readonly List<Entry> Pending = new List<Entry>();

        public static Entry Acquire(string normalisedText)
        {
            normalisedText = BlazonLimits.Drawable(normalisedText);
            string key = normalisedText + (normalisedText.IndexOf('[') >= 0 ? "|packs" + ChargePacks.Generation : "");
            if (!Entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { Key = key };
                Entries[key] = entry;
                if (normalisedText.Length == 0)
                {
                    entry.Material = TabardItem.CreateMaterial(Plain(), "Heraldry_Tabard_plain");
                    entry.Ready = true;
                }
                else
                {
                    var front = BlazonParser.Parse(normalisedText);
                    var back = BlazonParser.Parse(normalisedText);
                    back.Charges.Clear();
                    foreach (var o in back.Ordinaries) o.Charges.Clear();
                    IconArt.EnsureFor(front); // game icons are read on this (main) thread before the worker runs
                    entry.Job = Task.Run(() => Draw(front, back));
                    Pending.Add(entry);
                }
            }
            entry.Users++;
            return entry;
        }

        public static void Release(Entry entry)
        {
            if (entry == null || --entry.Users > 0) return;
            if (entry.Material != null)
            {
                var tex = entry.Material.mainTexture;
                UnityEngine.Object.Destroy(entry.Material);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
            entry.Material = null;
            Entries.Remove(entry.Key);
        }

        /// <summary>Main thread, once per frame: upload finished textures.</summary>
        public static void Pump()
        {
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var e = Pending[i];
                if (!e.Job.IsCompleted) continue;
                Pending.RemoveAt(i);
                if (e.Users <= 0) continue;
                if (e.Job.IsFaulted)
                {
                    Jotunn.Logger.LogError($"[Heraldry] Painting the tabard '{e.Key}' failed: {e.Job.Exception?.GetBaseException()}");
                    continue;
                }
                e.Material = TabardItem.CreateMaterial(Upload(e.Job.Result), "Heraldry_Tabard_" + e.Key);
                e.Ready = true;
            }
        }

        private static Color32[] Draw(Blazon front, Blazon back)
        {
            var px = new Color32[Size * Size];
            for (int i = 0; i < px.Length; i++) px[i] = Linen;
            Paint(px, TabardMesh.Front, front);
            Paint(px, TabardMesh.Back, back);
            return px;
        }

        /// <summary>Renders one panel at its own size and copies it into its place (Unity rows: bottom first).</summary>
        private static void Paint(Color32[] dst, TabardMesh.Panel panel, Blazon blazon)
        {
            int x0 = Mathf.FloorToInt(panel.UV.xMin * Size), y0 = Mathf.FloorToInt(panel.UV.yMin * Size);
            int x1 = Mathf.CeilToInt(panel.UV.xMax * Size), y1 = Mathf.CeilToInt(panel.UV.yMax * Size);
            int pw = x1 - x0, ph = y1 - y0;
            var mask = OutlineMask.FromTriangles(panel.OutlineUV, panel.OutlineTriangles, pw, ph);
            var art = FieldRenderer.Render(blazon, pw, ph, mask, FieldRenderer.Arrangement.Chest);
            for (int r = 0; r < ph; r++)
            {
                int ty = y1 - 1 - r; // the render's rows run top down
                if (ty < 0 || ty >= Size) continue;
                for (int x = 0; x < pw; x++)
                {
                    int tx = x0 + x;
                    if (tx < 0 || tx >= Size) continue;
                    var c = art[r * pw + x];
                    dst[ty * Size + tx] = new Color32(c.R, c.G, c.B, 255);
                }
            }
        }

        private static Texture2D Upload(Color32[] px)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Heraldry_Tabard", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixels32(px);
            tex.Apply(true, false);
            tex.Compress(true);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D Plain()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Heraldry_Tabard_plain" };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = Linen;
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
