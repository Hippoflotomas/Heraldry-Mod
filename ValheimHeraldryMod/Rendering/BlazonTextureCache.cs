using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     One texture per distinct (shape, size, blazon), shared by every banner showing it and destroyed when
    ///     the last one lets go. Banners must pair every Acquire with a Release (on text change and on destroy),
    ///     or textures leak across zone loads.
    ///     Parsing happens at once; the pixels are drawn on a worker thread and uploaded in <see cref="Pump"/>,
    ///     so writing on a banner never stalls the game. Until then the texture shows the field's first tincture.
    /// </summary>
    internal static class BlazonTextureCache
    {
        public sealed class Shape
        {
            public string Id;
            public float[] UV;
            public int[] Triangles;
            /// <summary>Fraction of the height, from the top, that can't be seen (behind the beam).</summary>
            public float HiddenTop;
            /// <summary>How plain charges use the surface: banners Spread, shields Compact.</summary>
            public FieldRenderer.Arrangement Arrangement = FieldRenderer.Arrangement.Spread;
            /// <summary>The field's centre as fractions from the top left; -1 = the middle.</summary>
            public float CentreU = -1f, CentreV = -1f;
        }

        private sealed class Entry
        {
            public Texture2D Texture;
            public int Users;
            public Task<Rgba[]> Job;
            public int Width, Height;
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        private static readonly List<Entry> Pending = new List<Entry>();
        private static readonly Regex Spaces = new Regex(@"\s+");
        internal static readonly Color32 Linen = new Color32(214, 204, 178, 255);

        /// <summary>Case and spacing don't change the arms, so they don't get their own texture.</summary>
        public static string Normalise(string text) =>
            string.IsNullOrWhiteSpace(text) ? "" : Spaces.Replace(text.Trim(), " ").ToLowerInvariant();

        public static int Count => Entries.Count;

        /// <summary>
        ///     Blazons with pack charges ("[wolf]") also key on the pack generation, so they redraw when packs arrive,
        ///     change or go away; everything else is unaffected.
        /// </summary>
        public static string KeyFor(string shapeId, string normalisedText, int width, int height) =>
            $"{shapeId}|{width}x{height}|{normalisedText}" +
            (normalisedText.IndexOf('[') >= 0 ? "|packs" + ChargeLibrary.Generation : "");

        public static Texture2D Acquire(string normalisedText, int width, int height, Shape shape, out string key)
        {
            normalisedText = BlazonLimits.Drawable(normalisedText);
            key = KeyFor(shape?.Id ?? "", normalisedText, width, height);
            if (!Entries.TryGetValue(key, out var entry))
            {
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, true)
                {
                    name = "Heraldry_" + key,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4,
                };
                entry = new Entry { Texture = tex, Width = width, Height = height };
                Entries[key] = entry;

                if (normalisedText.Length == 0)
                {
                    Fill(tex, Linen, true); // unpainted cloth
                }
                else
                {
                    var blazon = BlazonParser.Parse(normalisedText);
                    IconArt.EnsureFor(blazon); // game icons are read on this (main) thread before the worker runs
                    Fill(tex, PreviewColour(blazon), false);
                    float[] uv = shape?.UV;
                    int[] tris = shape?.Triangles;
                    float hidden = shape?.HiddenTop ?? 0f;
                    var arrangement = shape?.Arrangement ?? FieldRenderer.Arrangement.Spread;
                    float cu = shape?.CentreU ?? -1f, cv = shape?.CentreV ?? -1f;
                    entry.Job = Task.Run(() =>
                    {
                        var mask = OutlineMask.FromTriangles(uv, tris, width, height, hidden);
                        return FieldRenderer.Render(blazon, width, height, mask, arrangement, cu, cv);
                    });
                    Pending.Add(entry);
                }
            }
            entry.Users++;
            return entry.Texture;
        }

        public static void Release(string key)
        {
            if (key == null || !Entries.TryGetValue(key, out var entry))
                return;
            if (--entry.Users > 0)
                return;
            UnityEngine.Object.Destroy(entry.Texture);
            entry.Texture = null;  // a job still running will see this and drop its result
            Entries.Remove(key);
        }

        /// <summary>Main thread, once per frame: upload finished renders.</summary>
        public static void Pump()
        {
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var e = Pending[i];
                if (!e.Job.IsCompleted) continue;
                Pending.RemoveAt(i);
                if (e.Texture == null) continue; // released while rendering
                if (e.Job.IsFaulted)
                {
                    Jotunn.Logger.LogError($"[Heraldry] Rendering '{e.Texture.name}' failed: {e.Job.Exception?.GetBaseException()}");
                    continue;
                }

                // The renderer's rows run top to bottom; Unity's run bottom to top.
                var src = e.Job.Result;
                int w = e.Width, h = e.Height;
                var dst = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    int s = y * w, d = (h - 1 - y) * w;
                    for (int x = 0; x < w; x++)
                    {
                        var c = src[s + x];
                        dst[d + x] = new Color32(c.R, c.G, c.B, c.A); // gaps in tattered cloth are see-through
                    }
                }
                e.Texture.SetPixels32(dst);
                e.Texture.Apply(true, true); // mipmaps, then drop the CPU copy
            }
        }

        private static Color32 PreviewColour(Blazon b)
        {
            var t = b.Field != null && b.Field.Tinctures.Count > 0 ? b.Field.Tinctures[0] : null;
            if (t == null) return new Color32(255, 0, 255, 255);
            if (t.Kind == TinctureKind.Hex) return new Color32(t.Hex.R, t.Hex.G, t.Hex.B, 255);
            return Lexicon.Palette.TryGetValue(t.Name, out var c) ? new Color32(c.R, c.G, c.B, 255) : Linen;
        }

        private static void Fill(Texture2D tex, Color32 c, bool final)
        {
            var px = new Color32[tex.width * tex.height];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            tex.SetPixels32(px);
            tex.Apply(true, final);
        }
    }
}
