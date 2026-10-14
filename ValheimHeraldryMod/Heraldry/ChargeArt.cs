using System;
using System.Collections.Generic;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     A picture charge (a game icon now, charge-pack art later), prepared for the renderer:
    ///     its colours, a signed distance field of its outline, and an interior-lines layer.
    ///     The distance field is what lets picture charges share everything the geometric ones do:
    ///     silhouette, voided, fimbriated, counterchanged, flips, placement and sizing.
    ///     Built once per image (a few tens of milliseconds) and shared by every render.
    /// </summary>
    public sealed class ChargeArt
    {
        /// <summary>Working resolution. 64 px icons are upscaled smoothly to this before anything else.</summary>
        public const int Size = 256;

        // Interior lines from shading (tuned on all 72 trophies, see tools/HeraldryPreview): a pixel is "ink"
        // when it is this much darker than its blurred surroundings, away from the outline, in a patch big
        // enough not to be noise.
        private const float LineBlur = 6f, LineThreshold = 0.08f, LineEdgeGap = 4f, LineSoften = 0.8f;
        private const int LineMinArea = 40;

        public readonly string Key;
        private readonly float[] sdf;     // pixels, negative inside: body plus claws and tongue
        private readonly float[] sdfDisarmed, sdfMorne, sdfPlain; // the outline without parts of the mask
        private readonly float[] r, g, b; // straight colour, 0..1
        private readonly float[] lines;   // coverage 0..1; null = none
        private readonly float[] body;    // the colour image's own coverage (where "proper" has colours)
        private readonly float[] armed;   // mask green: claws, horns, beak... null = none
        private readonly float[] langued; // mask blue: tongue. null = none
        private readonly float cx, cy, scale; // charge box (-1..1) to art pixels

        public bool HasLines => lines != null;
        public bool HasArmed => armed != null;
        public bool HasLangued => langued != null;

        private ChargeArt(string key, float[][] sdfs, float[] r, float[] g, float[] b, float[] lines, float[] body,
            float[] armed, float[] langued, float cx, float cy, float scale)
        {
            Key = key;
            sdf = sdfs[0];
            sdfDisarmed = sdfs[1];
            sdfMorne = sdfs[2];
            sdfPlain = sdfs[3];
            this.r = r;
            this.g = g;
            this.b = b;
            this.lines = lines;
            this.body = body;
            this.armed = armed;
            this.langued = langued;
            this.cx = cx;
            this.cy = cy;
            this.scale = scale;
        }

        /// <param name="px">Straight-alpha pixels, rows top to bottom.</param>
        /// <param name="deriveLines">Make interior lines from the image's shading when the mask has none.</param>
        /// <param name="mask">
        ///     Optional, same size: red = interior lines, green = armed, blue = langued (each times its alpha).
        ///     Armed and langued parts belong to the outline even where the colour image is empty (a tongue in
        ///     an open mouth).
        /// </param>
        public static ChargeArt FromImage(string key, Rgba[] px, int w, int h, bool deriveLines, Rgba[] mask = null)
        {
            const int N = Size;
            if (mask != null && mask.Length != w * h) mask = null;
            // Fit the image in a square (centred), then upscale premultiplied colour with Catmull-Rom
            int side = Math.Max(w, h);
            int ox = (side - w) / 2, oy = (side - h) / 2;
            var srcA = new float[w * h];
            var srcR = new float[w * h];
            var srcG = new float[w * h];
            var srcB = new float[w * h];
            for (int i = 0; i < w * h; i++)
            {
                float ca = px[i].A / 255f;
                srcA[i] = ca;
                srcR[i] = ca * px[i].R / 255f;
                srcG[i] = ca * px[i].G / 255f;
                srcB[i] = ca * px[i].B / 255f;
            }
            var a = Upscale(srcA, w, h, side, ox, oy);
            var pr = Upscale(srcR, w, h, side, ox, oy);
            var pg = Upscale(srcG, w, h, side, ox, oy);
            var pb = Upscale(srcB, w, h, side, ox, oy);
            for (int i = 0; i < N * N; i++) a[i] = Clamp01(a[i]);

            // Straight colour back out of premultiplied
            for (int i = 0; i < N * N; i++)
            {
                float inv = a[i] > 0.004f ? 1f / a[i] : 0f;
                pr[i] = Clamp01(pr[i] * inv);
                pg[i] = Clamp01(pg[i] * inv);
                pb[i] = Clamp01(pb[i] * inv);
            }

            float[] maskLines = null, armed = null, langued = null;
            if (mask != null)
            {
                maskLines = MaskLayer(mask, w, h, side, ox, oy, 0);
                armed = MaskLayer(mask, w, h, side, ox, oy, 1);
                langued = MaskLayer(mask, w, h, side, ox, oy, 2);
            }
            var body = a;
            if (armed != null || langued != null)
            {
                a = (float[])body.Clone(); // the outline: body plus its claws and tongue
                for (int i = 0; i < N * N; i++)
                    a[i] = Math.Max(a[i], Math.Max(armed != null ? armed[i] : 0, langued != null ? langued[i] : 0));
            }

            var inside = new bool[N * N];
            int minX = N, minY = N, maxX = -1, maxY = -1;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                if (a[y * N + x] < 0.5f) continue;
                inside[y * N + x] = true;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            if (maxX < 0) return null; // nothing there

            var sdf = SignedDistance(inside, a, N);

            // Disarmed / morné cut the claws (and tongue) out of the silhouette, since claws usually sit on the
            // paws; plain is the colour image alone. All share the full outline's box, so nothing moves.
            float[] sdfDisarmed = sdf, sdfMorne = sdf, sdfPlain = sdf;
            if (armed != null || langued != null)
            {
                var noArms = new float[N * N];
                var noArmsOrTongue = new float[N * N];
                for (int i = 0; i < N * N; i++)
                {
                    float arm = armed != null ? armed[i] : 0, tongue = langued != null ? langued[i] : 0;
                    noArms[i] = Math.Max(Clamp01(body[i] - arm), tongue);
                    noArmsOrTongue[i] = Clamp01(body[i] - arm - tongue);
                }
                sdfDisarmed = armed != null ? SignedDistance(Inside(noArms), noArms, N) : sdf;
                sdfMorne = SignedDistance(Inside(noArmsOrTongue), noArmsOrTongue, N);
                sdfPlain = SignedDistance(Inside(body), body, N);
            }

            float[] lines = maskLines ?? (deriveLines ? DeriveLines(pr, pg, pb, body, sdf, N) : null);

            // The shape's larger extent spans the charge box the way a roundel does (-0.94..0.94)
            float bcx = (minX + maxX + 1) * 0.5f, bcy = (minY + maxY + 1) * 0.5f;
            float extent = Math.Max(maxX - minX + 1, maxY - minY + 1) * 0.5f;
            return new ChargeArt(key, new[] { sdf, sdfDisarmed, sdfMorne, sdfPlain }, pr, pg, pb, lines, body, armed, langued,
                bcx, bcy, extent / 0.94f);
        }

        private static bool[] Inside(float[] coverage)
        {
            var m = new bool[coverage.Length];
            for (int i = 0; i < m.Length; i++) m[i] = coverage[i] >= 0.5f;
            return m;
        }

        /// <summary>One plane, upscaled into the working square with Catmull-Rom (outside the image = 0).</summary>
        private static float[] Upscale(float[] src, int w, int h, int side, int ox, int oy)
        {
            const int N = Size;
            var dst = new float[N * N];
            float k = side / (float)N;
            // Shrinking a big image: average several samples per pixel instead of skipping detail
            int sub = Math.Max(1, (int)Math.Ceiling(k));
            float inv = 1f / (sub * sub);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float acc = 0;
                for (int j = 0; j < sub; j++)
                for (int i = 0; i < sub; i++)
                {
                    float sx = (x + (i + 0.5f) / sub) * k - 0.5f - ox, sy = (y + (j + 0.5f) / sub) * k - 0.5f - oy;
                    acc += Bicubic(src, w, h, sx, sy);
                }
                dst[y * N + x] = acc * inv;
            }
            return dst;
        }

        /// <summary>A mask channel (0 = red, 1 = green, 2 = blue) times its alpha, upscaled; null if it's empty.</summary>
        private static float[] MaskLayer(Rgba[] mask, int w, int h, int side, int ox, int oy, int channel)
        {
            var src = new float[w * h];
            bool any = false;
            for (int i = 0; i < w * h; i++)
            {
                var m = mask[i];
                byte c = channel == 0 ? m.R : channel == 1 ? m.G : m.B;
                // A layer pixel is one where this channel clearly dominates (anti-aliased edges keep their alpha)
                byte other = channel == 0 ? Math.Max(m.G, m.B) : channel == 1 ? Math.Max(m.R, m.B) : Math.Max(m.R, m.G);
                float v = c > 127 && c > other ? m.A / 255f : 0f;
                src[i] = v;
                if (v > 0.5f) any = true;
            }
            if (!any) return null;
            var up = Upscale(src, w, h, side, ox, oy);
            for (int i = 0; i < up.Length; i++) up[i] = Clamp01(up[i]);
            return up;
        }

        // ------------------------------------------------------------------ sampling (charge box units, v down)

        /// <summary>Signed distance in charge-box units; negative inside.</summary>
        public float Distance(float u, float v, MaskMode mode = MaskMode.Normal)
        {
            var sdf = mode == MaskMode.Disarmed ? sdfDisarmed : mode == MaskMode.Morne ? sdfMorne
                : mode == MaskMode.Plain ? sdfPlain : this.sdf;
            float x = cx + u * scale, y = cy + v * scale;
            const float pad = 1f;
            if (x < pad || y < pad || x > Size - 1 - pad || y > Size - 1 - pad)
            {
                // Outside the art: distance to its square, plus whatever the edge says
                float ex = Math.Max(Math.Max(pad - x, x - (Size - 1 - pad)), 0);
                float ey = Math.Max(Math.Max(pad - y, y - (Size - 1 - pad)), 0);
                float edge = Bilinear(sdf, Clamp(x, pad, Size - 1 - pad), Clamp(y, pad, Size - 1 - pad));
                return (Math.Max(edge, 0) + (float)Math.Sqrt(ex * ex + ey * ey)) / scale;
            }
            return Bilinear(sdf, x, y) / scale;
        }

        public Rgba Colour(float u, float v)
        {
            float x = Clamp(cx + u * scale, 0, Size - 1), y = Clamp(cy + v * scale, 0, Size - 1);
            return new Rgba(ToByte(Bilinear(r, x, y)), ToByte(Bilinear(g, x, y)), ToByte(Bilinear(b, x, y)));
        }

        /// <summary>How much of this point is the colour image itself (0..1), rather than an armed/langued part.</summary>
        public float Body(float u, float v) => Sample(body, u, v);

        /// <summary>Armed (claws, horns, beak) coverage 0..1.</summary>
        public float Armed(float u, float v) => Sample(armed, u, v);

        /// <summary>Langued (tongue) coverage 0..1.</summary>
        public float Langued(float u, float v) => Sample(langued, u, v);

        private float Sample(float[] plane, float u, float v)
        {
            if (plane == null) return 0;
            float x = cx + u * scale, y = cy + v * scale;
            if (x < 0 || y < 0 || x > Size - 1 || y > Size - 1) return 0;
            return Bilinear(plane, x, y);
        }

        /// <summary>Interior-line coverage 0..1.</summary>
        public float Lines(float u, float v)
        {
            if (lines == null) return 0;
            float x = cx + u * scale, y = cy + v * scale;
            if (x < 0 || y < 0 || x > Size - 1 || y > Size - 1) return 0;
            return Bilinear(lines, x, y);
        }

        // ------------------------------------------------------------------ building

        /// <summary>
        ///     Exact Euclidean distance transform both ways (Felzenszwalb-Huttenlocher), refined at the edge by
        ///     the coverage so the outline sits between pixels instead of on them.
        /// </summary>
        private static float[] SignedDistance(bool[] inside, float[] alpha, int n)
        {
            var outsideSq = Edt(inside, n, true);  // distance from outside pixels to the shape
            var insideSq = Edt(inside, n, false);  // distance from inside pixels to the background
            var d = new float[n * n];
            for (int i = 0; i < n * n; i++)
            {
                float v = inside[i] ? -((float)Math.Sqrt(insideSq[i]) - 0.5f) : (float)Math.Sqrt(outsideSq[i]) - 0.5f;
                // Pixels at the edge: use the coverage, which is smoother than the stair-step
                if (Math.Abs(v) <= 1f) v = 0.5f - alpha[i];
                d[i] = v;
            }
            return d;
        }

        /// <summary>Squared distance to the nearest pixel where inside == target.</summary>
        private static float[] Edt(bool[] inside, int n, bool target)
        {
            const float Inf = 1e20f;
            var f = new float[n * n];
            for (int i = 0; i < n * n; i++) f[i] = inside[i] == target ? 0 : Inf;
            var col = new float[n];
            var res = new float[n];
            var v = new int[n];
            var z = new float[n + 1];
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++) col[y] = f[y * n + x];
                Edt1(col, res, n, v, z);
                for (int y = 0; y < n; y++) f[y * n + x] = res[y];
            }
            for (int y = 0; y < n; y++)
            {
                Array.Copy(f, y * n, col, 0, n);
                Edt1(col, res, n, v, z);
                Array.Copy(res, 0, f, y * n, n);
            }
            return f;
        }

        private static void Edt1(float[] f, float[] d, int n, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                d[q] = (q - v[k]) * (q - v[k]) + f[v[k]];
            }
        }

        private static float[] DeriveLines(float[] r, float[] g, float[] b, float[] a, float[] sdf, int n)
        {
            var lum = new float[n * n];
            var la = new float[n * n];
            for (int i = 0; i < n * n; i++)
            {
                lum[i] = 0.299f * r[i] + 0.587f * g[i] + 0.114f * b[i];
                la[i] = lum[i] * a[i];
            }
            var blurLa = Blur(la, n, LineBlur);
            var blurA = Blur(a, n, LineBlur);

            var ink = new bool[n * n];
            for (int i = 0; i < n * n; i++)
            {
                float local = blurLa[i] / Math.Max(blurA[i], 1e-3f);
                ink[i] = local - lum[i] > LineThreshold && sdf[i] < -LineEdgeGap;
            }
            RemoveSmall(ink, n, LineMinArea);

            var cov = new float[n * n];
            bool any = false;
            for (int i = 0; i < n * n; i++)
                if (ink[i]) { cov[i] = 1; any = true; }
            return any ? Blur(cov, n, LineSoften) : null;
        }

        /// <summary>Drops 4-connected patches smaller than minArea.</summary>
        private static void RemoveSmall(bool[] m, int n, int minArea)
        {
            var seen = new bool[n * n];
            var stack = new Stack<int>();
            var patch = new List<int>();
            for (int s = 0; s < n * n; s++)
            {
                if (!m[s] || seen[s]) continue;
                patch.Clear();
                stack.Push(s);
                seen[s] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    patch.Add(i);
                    int x = i % n, y = i / n;
                    if (x > 0) Visit(i - 1);
                    if (x < n - 1) Visit(i + 1);
                    if (y > 0) Visit(i - n);
                    if (y < n - 1) Visit(i + n);
                }
                if (patch.Count < minArea)
                    foreach (int i in patch) m[i] = false;
            }

            void Visit(int j)
            {
                if (!m[j] || seen[j]) return;
                seen[j] = true;
                stack.Push(j);
            }
        }

        /// <summary>Separable Gaussian blur, edges clamped.</summary>
        private static float[] Blur(float[] src, int n, float sigma)
        {
            int rad = Math.Max(1, (int)Math.Ceiling(sigma * 3));
            var kern = new float[rad * 2 + 1];
            float sum = 0;
            for (int i = -rad; i <= rad; i++) sum += kern[i + rad] = (float)Math.Exp(-i * i / (2 * sigma * sigma));
            for (int i = 0; i < kern.Length; i++) kern[i] /= sum;
            var tmp = new float[n * n];
            var dst = new float[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float acc = 0;
                for (int k = -rad; k <= rad; k++) acc += src[y * n + Math.Min(n - 1, Math.Max(0, x + k))] * kern[k + rad];
                tmp[y * n + x] = acc;
            }
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float acc = 0;
                for (int k = -rad; k <= rad; k++) acc += tmp[Math.Min(n - 1, Math.Max(0, y + k)) * n + x] * kern[k + rad];
                dst[y * n + x] = acc;
            }
            return dst;
        }

        /// <summary>
        ///     Catmull-Rom sample of one plane; outside the image is 0.
        /// </summary>
        private static float Bicubic(float[] src, int w, int h, float x, float y)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            float acc = 0;
            for (int j = -1; j <= 2; j++)
            {
                float wy = CatmullRom(j - fy);
                int yy = y0 + j;
                if (yy < 0 || yy >= h) continue;
                for (int i = -1; i <= 2; i++)
                {
                    int xx = x0 + i;
                    if (xx < 0 || xx >= w) continue;
                    acc += CatmullRom(i - fx) * wy * src[yy * w + xx];
                }
            }
            return acc;
        }

        private static float CatmullRom(float t)
        {
            t = Math.Abs(t);
            if (t < 1) return 1.5f * t * t * t - 2.5f * t * t + 1;
            if (t < 2) return -0.5f * t * t * t + 2.5f * t * t - 4 * t + 2;
            return 0;
        }

        private static float Bilinear(float[] f, float x, float y)
        {
            int x0 = (int)x, y0 = (int)y;
            int x1 = Math.Min(Size - 1, x0 + 1), y1 = Math.Min(Size - 1, y0 + 1);
            float tx = x - x0, ty = y - y0;
            float top = f[y0 * Size + x0] + (f[y0 * Size + x1] - f[y0 * Size + x0]) * tx;
            float bottom = f[y1 * Size + x0] + (f[y1 * Size + x1] - f[y1 * Size + x0]) * tx;
            return top + (bottom - top) * ty;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
        private static byte ToByte(float v) => (byte)(Clamp01(v) * 255f + 0.5f);
    }

    /// <summary>
    ///     Every picture charge available on this client, by key ("trophywolf"). The game side registers raw
    ///     images on the main thread; the art itself is built lazily, once, by whichever render needs it first.
    ///     Thread-safe.
    /// </summary>
    public static class ChargeArtLibrary
    {
        private sealed class Entry
        {
            public Rgba[] Pixels, Mask;
            public int W, H;
            public bool DeriveLines;
            public ChargeArt Art;
            public bool Built;
        }

        private static readonly object Lock = new object();
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();

        /// <summary>
        ///     Keys the server doesn't allow (the admin exclude list); they render as the fallback roundel.
        ///     Set by the game side; must be safe to call from worker threads.
        /// </summary>
        public static Func<string, bool> IsBlocked = key => false;

        public static void Register(string key, Rgba[] pixels, int w, int h, bool deriveLines, Rgba[] mask = null)
        {
            lock (Lock) Entries[key] = new Entry { Pixels = pixels, Mask = mask, W = w, H = h, DeriveLines = deriveLines };
        }

        public static bool Has(string key)
        {
            lock (Lock) return Entries.ContainsKey(key);
        }

        /// <summary>The art for a key, or null (unknown, blocked, or an empty image).</summary>
        public static ChargeArt Get(string key)
        {
            if (key == null || IsBlocked(key)) return null;
            lock (Lock)
            {
                if (!Entries.TryGetValue(key, out var e)) return null;
                if (!e.Built)
                {
                    e.Art = ChargeArt.FromImage(key, e.Pixels, e.W, e.H, e.DeriveLines, e.Mask);
                    e.Built = true;
                    e.Pixels = null;
                    e.Mask = null;
                }
                return e.Art;
            }
        }
    }
}
