using System;
using System.Collections.Generic;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     Lettering for inscriptions (2026-10-14): one alpha mask per text, made by the game side from its own
    ///     Norse font (runes included) on the main thread, then used by any render. Rows top to bottom.
    ///     A text without a mask yet is simply not drawn (the scroll or ordinary shows plain).
    /// </summary>
    public static class TextArtLibrary
    {
        public sealed class Mask
        {
            public byte[] A;
            public int W, H;
            /// <summary>0: A is coverage. Else A is a signed distance field (0.5 = the edge) spanning this many mask pixels.</summary>
            public float SdfSpread;
        }

        private static readonly object Lock = new object();
        private static readonly Dictionary<string, Mask> Masks = new Dictionary<string, Mask>();

        public static void Register(string text, byte[] alpha, int w, int h, float sdfSpread = 0f)
        {
            if (alpha == null || alpha.Length != w * h) return;
            lock (Lock) Masks[text] = new Mask { A = alpha, W = w, H = h, SdfSpread = sdfSpread };
        }

        public static bool Has(string text)
        {
            lock (Lock) return Masks.ContainsKey(text);
        }

        public static Mask Get(string text)
        {
            lock (Lock) return text != null && Masks.TryGetValue(text, out var m) ? m : null;
        }

        /// <summary>Coverage at (fx, fy) in 0..1 of the mask, bilinear.</summary>
        public static float Sample(Mask m, float fx, float fy)
        {
            float x = fx * m.W - 0.5f, y = fy * m.H - 0.5f;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float tx = x - x0, ty = y - y0;
            float A(int xx, int yy) => xx < 0 || yy < 0 || xx >= m.W || yy >= m.H ? 0f : m.A[yy * m.W + xx] / 255f;
            float top = A(x0, y0) + (A(x0 + 1, y0) - A(x0, y0)) * tx;
            float bot = A(x0, y0 + 1) + (A(x0 + 1, y0 + 1) - A(x0, y0 + 1)) * tx;
            return top + (bot - top) * ty;
        }
    }

    public sealed partial class FieldRenderer
    {
        /// <summary>
        ///     Words in a box: (lu, lv) is the point in the box's own axes (lu along the text, lv down across it),
        ///     in pixels from the box centre. The text keeps its proportions and is centred.
        /// </summary>
        private Rgba Inscribe(Rgba under, Inscription ins, float lu, float lv, float halfW, float halfH, int region, float x, float y)
        {
            if (ins == null || ins.Hidden) return under;
            var mask = TextArtLibrary.Get(ins.Display);
            if (mask == null || mask.W == 0 || mask.H == 0) return under;
            float aspect = mask.W / (float)mask.H;
            float tw = 2 * halfW, th = 2 * halfH;
            if (tw / th > aspect) tw = th * aspect; else th = tw / aspect;
            float fx = lu / tw + 0.5f, fy = lv / th + 0.5f;
            if (fx < 0 || fx > 1 || fy < 0 || fy > 1) return under;
            float a = TextArtLibrary.Sample(mask, fx, fy);
            if (mask.SdfSpread > 0)
            {
                // A distance field: a crisp edge one output pixel wide at any size
                float scale = th / mask.H; // output pixels per mask pixel
                a = Math.Max(0f, Math.Min(1f, (a - 0.5f) * mask.SdfSpread * scale + 0.5f));
            }
            if (a < 0.01f) return under;
            // Default ink: sable on metals and light colours, argent on dark ones (like interior lines)
            var ink = ins.Tincture != null ? Tinct(ins.Tincture, region, x, y)
                    : Luma(under) < 110 ? Lexicon.Palette["argent"] : Lexicon.Palette["sable"];
            return Mix(under, ink, a);
        }

        /// <summary>Words along an ordinary (fess, chief, cross, pale, bend), within its band.</summary>
        private Rgba OrdinaryText(Ordinary o, Rgba c, int region, float x, float y)
        {
            float hw = (o.Diminutive ? DiminutiveWidth : BandWidth) * unit * 0.5f;
            switch (o.Kind)
            {
                case OrdinaryKind.Chief:
                {
                    float depth = ChiefDepth * fh;
                    return Inscribe(c, o.Inscription, x - w * 0.5f, y - depth * 0.5f, w * 0.44f, depth * 0.32f, region, x, y);
                }
                case OrdinaryKind.Fess:
                case OrdinaryKind.Cross: // along the bar across
                {
                    float sx = WX(x), sy = WY(y);
                    return Inscribe(c, o.Inscription, sx - w * 0.5f, sy - fh * 0.5f, w * 0.44f, hw * 0.72f, region, x, y);
                }
                case OrdinaryKind.Pale:
                {
                    // Turned to read downwards, the tops of the letters towards dexter... as seen: towards the viewer's right
                    float sx = WX(x), sy = WY(y);
                    return Inscribe(c, o.Inscription, sy - fh * 0.5f, -(sx - w * 0.5f), fh * 0.44f, hw * 0.72f, region, x, y);
                }
                case OrdinaryKind.Bend:
                case OrdinaryKind.BendSinister:
                {
                    float sx = WX(x) - w * 0.5f, sy = WY(y) - fh * 0.5f;
                    float dx = w / diag, dy = (o.Kind == OrdinaryKind.Bend ? fh : -fh) / diag;
                    float lu = sx * dx + sy * dy, lv = sx * -dy + sy * dx;
                    return Inscribe(c, o.Inscription, lu, lv, diag * 0.42f, hw * 0.72f, region, x, y);
                }
            }
            return c;
        }

        /// <summary>A scroll's folds (darker lines where the ends turn under) and its words.</summary>
        private Rgba ScrollDetail(Placed p, Rgba c, float u, float v, int region, float x, float y)
        {
            float centre = ChargeShapes.ScrollCurve(u);
            float fold = Math.Abs(Math.Abs(u) - ChargeShapes.ScrollFold);
            if (fold < 0.012f + 1.2f / p.R)
                c = Mix(c, Luma(c) < 110 ? Lexicon.Palette["argent"] : Lexicon.Palette["sable"], 0.55f);
            // The ends behind the body are a touch darker
            if (Math.Abs(u) > ChargeShapes.ScrollFold) c = Mix(c, new Rgba(0, 0, 0), 0.18f);
            if (p.C.Inscription == null || Math.Abs(u) > ChargeShapes.ScrollFold - 0.04f) return c;
            return Inscribe(c, p.C.Inscription, u * p.R, (v - centre) * p.R,
                (ChargeShapes.ScrollFold - 0.06f) * p.R, ChargeShapes.ScrollHalf * 0.72f * p.R, region, x, y);
        }
    }
}
