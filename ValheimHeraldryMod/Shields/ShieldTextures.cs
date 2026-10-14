using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;
using ValheimHeraldryMod.Rendering;

namespace ValheimHeraldryMod.Shields
{
    /// <summary>
    ///     One painted texture per (shield model, blazon), shared by every shield showing it and destroyed when the
    ///     last lets go. The arms are drawn flat (worker thread), then baked through the shield's own UVs:
    ///     <list type="bullet">
    ///         <item>shields with paint styles: into a paint-style atlas, the same arms in all 16 cells so whatever
    ///         style the shield was made with shows them; outside the face it's transparent, so the vanilla wood,
    ///         metal and relief show as usual;</item>
    ///         <item>shields without: over a copy of the shield's own texture.</item>
    ///     </list>
    ///     Each entry also keeps copies of the shield's materials with that texture in, for renderers to swap to.
    /// </summary>
    internal static class ShieldTextures
    {
        private const int PatternSize = 512; // longest side of the flat arms
        private const int CellSize = 512;    // one style cell; the atlas is 4x4 cells
        private const int MaxMainSize = 2048;

        public sealed class Entry
        {
            public string Key;
            public ShieldFace Face;
            public Texture2D Texture;
            public int Users;
            public bool Ready;
            internal Task<Color32[]> Job;
            internal int Width, Height;
            private readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();

            /// <summary>The painted version of one of the shield's materials (itself if it isn't painted).</summary>
            public Material Painted(Material original)
            {
                if (!Ready || original == null) return original;
                bool target = Face.UsesStyleSlot ? ShieldFace.HasStyleSlot(original) : original.HasProperty("_MainTex");
                if (!target) return original;
                if (!copies.TryGetValue(original, out var copy))
                {
                    copy = new Material(original) { name = original.name + "_heraldry" };
                    copy.SetTexture(Face.UsesStyleSlot ? "_StyleTex" : "_MainTex", Texture);
                    copies[original] = copy;
                }
                return copy;
            }

            internal void Destroy()
            {
                foreach (var m in copies.Values) if (m != null) UnityEngine.Object.Destroy(m);
                copies.Clear();
                if (Texture != null) UnityEngine.Object.Destroy(Texture);
                Texture = null;
            }
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        private static readonly List<Entry> Pending = new List<Entry>();

        public static Entry Acquire(ShieldFace face, string normalisedText)
        {
            normalisedText = BlazonLimits.Drawable(normalisedText);
            string key = face.Key + "|" + normalisedText + (normalisedText.IndexOf('[') >= 0 ? "|packs" + ChargeLibrary.Generation : "");
            if (!Entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { Key = key, Face = face };
                Entries[key] = entry;

                var blazon = BlazonParser.Parse(normalisedText);
                IconArt.EnsureFor(blazon); // game icons are read on this (main) thread before the worker runs
                int pw = face.Aspect >= 1f ? PatternSize : Mathf.Max(32, Mathf.RoundToInt(PatternSize * face.Aspect));
                int ph = face.Aspect >= 1f ? Mathf.Max(32, Mathf.RoundToInt(PatternSize / face.Aspect)) : PatternSize;

                Color32[] background = null;
                int w, h;
                if (face.UsesStyleSlot) { w = h = CellSize; }
                else
                {
                    w = Mathf.Min(MaxMainSize, face.MainTex.width);
                    h = Mathf.Min(MaxMainSize, face.MainTex.height);
                    background = Textures.ReadPixels(face.MainTex, w, h, linear: false);
                }
                entry.Width = w; entry.Height = h;
                entry.Job = Task.Run(() =>
                {
                    var outline = OutlineMask.FromTriangles(face.OutlineUV, face.OutlineTriangles, pw, ph);
                    var arms = FieldRenderer.Render(blazon, pw, ph, outline, FieldRenderer.Arrangement.Compact, face.CentreU, face.CentreV);
                    return Bake(face, arms, pw, ph, w, h, background);
                });
                Pending.Add(entry);
            }
            entry.Users++;
            return entry;
        }

        public static void Release(Entry entry)
        {
            if (entry == null || --entry.Users > 0) return;
            entry.Destroy();
            Entries.Remove(entry.Key);
        }

        /// <summary>Main thread, once per frame: upload finished bakes.</summary>
        public static void Pump()
        {
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var e = Pending[i];
                if (!e.Job.IsCompleted) continue;
                Pending.RemoveAt(i);
                if (e.Users <= 0) continue; // released while baking
                if (e.Job.IsFaulted)
                {
                    Jotunn.Logger.LogError($"[Heraldry] Painting '{e.Key}' failed: {e.Job.Exception?.GetBaseException()}");
                    continue;
                }
                var px = e.Job.Result;
                Texture2D tex;
                if (e.Face.UsesStyleSlot)
                {
                    // The same cell in all 16 places of the atlas
                    int atlas = CellSize * 4;
                    var all = new Color32[atlas * atlas];
                    for (int c = 0; c < 16; c++)
                    {
                        int ox = (c % 4) * CellSize, oy = (c / 4) * CellSize;
                        for (int y = 0; y < CellSize; y++) Array.Copy(px, y * CellSize, all, (oy + y) * atlas + ox, CellSize);
                    }
                    tex = new Texture2D(atlas, atlas, TextureFormat.RGBA32, true);
                    tex.SetPixels32(all);
                }
                else
                {
                    tex = new Texture2D(e.Width, e.Height, TextureFormat.RGBA32, true);
                    tex.SetPixels32(px);
                }
                tex.name = "Heraldry_" + e.Key;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 4;
                tex.Apply(true, false);
                if (tex.width % 4 == 0 && tex.height % 4 == 0) tex.Compress(true); // 16 MB -> 4 MB for an atlas
                tex.Apply(false, true);
                e.Texture = tex;
                e.Ready = true;
            }
        }

        /// <summary>
        ///     Draws the flat arms into the shield's UV layout: every pixel inside a face triangle (in UV space) takes
        ///     the arms' colour at the same point of the straight-on picture. Rows bottom first (Unity's order).
        /// </summary>
        private static Color32[] Bake(ShieldFace face, Rgba[] arms, int pw, int ph, int w, int h, Color32[] background)
        {
            var dst = background ?? new Color32[w * h]; // transparent where we don't paint
            foreach (var t in face.Front)
            {
                Raster(t.UvA, t.UvB, t.UvC, w, h, 0.75f, (x, y, wa, wb, wc) =>
                {
                    var p = t.PA * wa + t.PB * wb + t.PC * wc;
                    var c = Sample(arms, pw, ph, p.x, 1f - p.y); // the arms' rows run top down
                    if (c.A < 128) return; // tattered: the paint is worn off here, the shield shows
                    byte a = 255;
                    if (face.PaintMask != null)
                    {
                        int mx = Mathf.Clamp(x * face.MaskCell / w, 0, face.MaskCell - 1);
                        int my = Mathf.Clamp(y * face.MaskCell / h, 0, face.MaskCell - 1);
                        float f = Mathf.Clamp01((face.PaintMask[my * face.MaskCell + mx] - ShieldFace.MaskLow) / (float)(ShieldFace.MaskHigh - ShieldFace.MaskLow));
                        a = (byte)Mathf.RoundToInt(255 * f);
                        if (a == 0) return;
                    }
                    int i = y * w + x;
                    if (background != null && a < 255)
                    {
                        var b = background[i];
                        float k = a / 255f;
                        dst[i] = new Color32((byte)(b.r + (c.R - b.r) * k), (byte)(b.g + (c.G - b.g) * k), (byte)(b.b + (c.B - b.b) * k), b.a);
                    }
                    else dst[i] = new Color32(c.R, c.G, c.B, background != null ? background[i].a : a);
                });
            }
            return dst;
        }

        /// <summary>Bilinear sample of the arms; u, v in 0..1 with v = 0 the top row.</summary>
        private static Rgba Sample(Rgba[] px, int w, int h, float u, float v)
        {
            float fx = Mathf.Clamp(u * w - 0.5f, 0f, w - 1), fy = Mathf.Clamp(v * h - 0.5f, 0f, h - 1);
            int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            float tx = fx - x0, ty = fy - y0;
            Rgba a = px[y0 * w + x0], b = px[y0 * w + x1], c = px[y1 * w + x0], d = px[y1 * w + x1];
            byte L(byte p, byte q, byte r, byte s) { float top = p + (q - p) * tx, bot = r + (s - r) * tx; return (byte)Mathf.Clamp(Mathf.RoundToInt(top + (bot - top) * ty), 0, 255); }
            return new Rgba(L(a.R, b.R, c.R, d.R), L(a.G, b.G, c.G, d.G), L(a.B, b.B, c.B, d.B), L(a.A, b.A, c.A, d.A));
        }

        private delegate void PixelVisitor(int x, int y, float wa, float wb, float wc);

        /// <summary>Every pixel whose centre is inside the triangle, or within <paramref name="pad"/> pixels of it (closes seams). From ShieldShare.</summary>
        private static void Raster(Vector2 a, Vector2 b, Vector2 c, int width, int height, float pad, PixelVisitor visit)
        {
            var A = new Vector2(a.x * width, a.y * height);
            var B = new Vector2(b.x * width, b.y * height);
            var C = new Vector2(c.x * width, c.y * height);
            float area2 = Cross(B - A, C - A);
            if (Mathf.Abs(area2) < 1e-6f) return;
            float hA = Mathf.Abs(area2) / Mathf.Max((C - B).magnitude, 1e-6f);
            float hB = Mathf.Abs(area2) / Mathf.Max((A - C).magnitude, 1e-6f);
            float hC = Mathf.Abs(area2) / Mathf.Max((B - A).magnitude, 1e-6f);
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x)) - pad));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x)) + pad));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y)) - pad));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y)) + pad));
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    var P = new Vector2(x + 0.5f, y + 0.5f);
                    float wa = Cross(C - B, P - B) / area2, wb = Cross(A - C, P - C) / area2, wc = 1f - wa - wb;
                    if (wa * hA < -pad || wb * hB < -pad || wc * hC < -pad) continue;
                    visit(x, y, wa, wb, wc);
                }
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
