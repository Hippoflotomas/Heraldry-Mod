using System;
using System.Collections.Generic;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>The five paints, mixed like real paint: red, yellow and blue, plus black and white.</summary>
    public enum Paint { Red, Yellow, Blue, Black, White }

    /// <summary>
    ///     What a blazon costs to paint (Hippo, 2026-10-14). Every tincture named costs the same total
    ///     (<c>perTincture</c>, e.g. 4), split by how it's mixed:
    ///     <list type="bullet">
    ///         <item>the colour's white part (its lowest RGB channel) in white paint,</item>
    ///         <item>its black part (100% minus its highest channel) in black paint,</item>
    ///         <item>the rest (its pure colour) in red, yellow and blue, converted from light mixing (RGB) to paint
    ///         mixing (RYB), so or is yellow and vert is half blue, half yellow.</item>
    ///     </list>
    ///     Each tincture is paid once, however much of the arms it covers. Furs pay for both their colours, proper
    ///     costs nothing. When the total passes <c>maxTotal</c>, every paint is scaled down by the same factor.
    /// </summary>
    public static class PaintCost
    {
        public static readonly Paint[] All = { Paint.Red, Paint.Yellow, Paint.Blue, Paint.Black, Paint.White };

        // The pure colour each tincture stands for, for costing (not the muted cloth palette it's drawn in:
        // gules is plain red here, so it costs plain red paint)
        private static readonly Dictionary<string, double[]> Ideal = new Dictionary<string, double[]>
        {
            { "or", new[] { 1.0, 1.0, 0.0 } },
            { "argent", new[] { 1.0, 1.0, 1.0 } },
            { "azure", new[] { 0.0, 0.0, 1.0 } },
            { "gules", new[] { 1.0, 0.0, 0.0 } },
            { "sable", new[] { 0.0, 0.0, 0.0 } },
            { "vert", new[] { 0.0, 1.0, 0.0 } },
            { "purpure", new[] { 1.0, 0.0, 1.0 } },  // red and blue paint (a hex purple can be darker)
            { "tenne", new[] { 1.0, 0.5, 0.0 } },     // orange
            { "sanguine", new[] { 0.6, 0.0, 0.0 } },  // blood red
            { "murrey", new[] { 0.55, 0.0, 0.3 } },   // mulberry
            { "magenta", new[] { 1.0, 0.0, 1.0 } },
        };

        // Furs: the tinctures they're made of
        private static readonly Dictionary<string, string[]> Furs = new Dictionary<string, string[]>
        {
            { "ermine", new[] { "argent", "sable" } },
            { "ermines", new[] { "sable", "argent" } },
            { "erminois", new[] { "or", "sable" } },
            { "pean", new[] { "sable", "or" } },
            { "vair", new[] { "argent", "azure" } },
            { "counter-vair", new[] { "argent", "azure" } },
            { "potent", new[] { "argent", "azure" } },
            { "counter-potent", new[] { "argent", "azure" } },
        };

        /// <summary>The paint a blazon needs, by paint; empty if nothing (no tinctures, or only proper).</summary>
        public static Dictionary<Paint, int> For(Blazon blazon, int perTincture, int maxTotal)
        {
            var result = new Dictionary<Paint, int>();
            if (blazon == null || perTincture <= 0) return result;

            // Every distinct colour used (as RGB), so "argent" twice, or argent plus ermine's argent, is paid once
            var colours = new Dictionary<string, double[]>();
            foreach (var t in Tinctures(blazon)) AddTincture(colours, t);

            var total = new double[All.Length];
            foreach (var rgb in colours.Values)
            {
                var parts = Split(rgb);
                var whole = Distribute(parts, perTincture);
                for (int i = 0; i < whole.Length; i++) total[i] += whole[i];
            }

            int[] amounts = new int[All.Length];
            for (int i = 0; i < amounts.Length; i++) amounts[i] = (int)Math.Round(total[i]);
            int sum = 0;
            foreach (int a in amounts) sum += a;
            if (maxTotal > 0 && sum > maxTotal) amounts = Scale(amounts, sum, maxTotal);

            for (int i = 0; i < All.Length; i++)
                if (amounts[i] > 0) result[All[i]] = amounts[i];
            return result;
        }

        /// <summary>
        ///     One tincture's colour as shares of the five paints, adding up to 1:
        ///     red, yellow, blue, black, white (the order of <see cref="All"/>).
        /// </summary>
        public static double[] Split(double[] rgb)
        {
            double r = Clamp01(rgb[0]), g = Clamp01(rgb[1]), b = Clamp01(rgb[2]);
            double min = Math.Min(r, Math.Min(g, b)), max = Math.Max(r, Math.Max(g, b));
            double white = min, black = 1.0 - max, chroma = max - min;
            var shares = new double[5];
            shares[3] = black;
            shares[4] = white;
            if (chroma > 1e-6)
            {
                // RGB -> RYB (after Sugita & Takahashi), white already removed; only the proportions are kept
                r -= min; g -= min; b -= min;
                double y = Math.Min(r, g);
                r -= y; g -= y;
                if (b > 0 && g > 0) { b /= 2; g /= 2; }
                y += g; b += g;
                double sum = r + y + b;
                if (sum > 1e-9)
                {
                    shares[0] = chroma * r / sum;
                    shares[1] = chroma * y / sum;
                    shares[2] = chroma * b / sum;
                }
            }
            return shares;
        }

        /// <summary>Whole numbers of paint adding up to exactly <paramref name="total"/>: largest remainders first.</summary>
        private static int[] Distribute(double[] shares, int total)
        {
            var whole = new int[shares.Length];
            var rest = new double[shares.Length];
            int used = 0;
            for (int i = 0; i < shares.Length; i++)
            {
                double exact = shares[i] * total;
                whole[i] = (int)Math.Floor(exact + 1e-9);
                rest[i] = exact - whole[i];
                used += whole[i];
            }
            for (; used < total; used++)
            {
                int best = -1;
                for (int i = 0; i < rest.Length; i++)
                    if (rest[i] > 1e-9 && (best < 0 || rest[i] > rest[best])) best = i;
                if (best < 0) break;
                whole[best]++;
                rest[best] = 0;
            }
            return whole;
        }

        /// <summary>
        ///     Shrinks the amounts to <paramref name="max"/> in all, keeping the mix: every paint scaled by the same
        ///     factor, largest remainders rounded up, and every paint that was needed keeps at least one (when the
        ///     ceiling has room for that many).
        /// </summary>
        private static int[] Scale(int[] amounts, int sum, int max)
        {
            var shares = new double[amounts.Length];
            for (int i = 0; i < amounts.Length; i++) shares[i] = (double)amounts[i] / sum;
            var scaled = Distribute(shares, max);
            int kinds = 0;
            foreach (int a in amounts) if (a > 0) kinds++;
            if (kinds <= max)
            {
                for (int i = 0; i < scaled.Length; i++)
                {
                    if (amounts[i] == 0 || scaled[i] > 0) continue;
                    int biggest = 0;
                    for (int j = 1; j < scaled.Length; j++) if (scaled[j] > scaled[biggest]) biggest = j;
                    scaled[biggest]--;
                    scaled[i] = 1;
                }
            }
            return scaled;
        }

        private static void AddTincture(Dictionary<string, double[]> colours, Tincture t)
        {
            if (t == null) return;
            switch (t.Kind)
            {
                case TinctureKind.Proper:
                case TinctureKind.Counterchanged:
                    return;
                case TinctureKind.Hex:
                    colours[t.Hex.ToString()] = new[] { t.Hex.R / 255.0, t.Hex.G / 255.0, t.Hex.B / 255.0 };
                    return;
                case TinctureKind.Fur:
                    if (Furs.TryGetValue(t.Name, out var parts))
                        foreach (var p in parts) colours[p] = Ideal[p];
                    return;
                default:
                    if (Ideal.TryGetValue(t.Name, out var rgb)) colours[t.Name] = rgb;
                    else if (Lexicon.Palette.TryGetValue(t.Name, out var c)) colours[t.Name] = new[] { c.R / 255.0, c.G / 255.0, c.B / 255.0 };
                    return;
            }
        }

        /// <summary>Every tincture the arms use: field, ordinaries, charges, edgings, claws and tongues.</summary>
        private static IEnumerable<Tincture> Tinctures(Blazon b)
        {
            if (b.Field != null)
                foreach (var t in b.Field.Tinctures) yield return t;
            foreach (var o in b.Ordinaries)
            {
                yield return o.Tincture;
                foreach (var t in StyleTinctures(o.Style)) yield return t;
                foreach (var c in o.Charges)
                    foreach (var t in ChargeTinctures(c)) yield return t;
            }
            foreach (var c in b.Charges)
                foreach (var t in ChargeTinctures(c)) yield return t;
        }

        private static IEnumerable<Tincture> ChargeTinctures(Charge c)
        {
            yield return c.Tincture;
            yield return c.Armed;
            yield return c.Langued;
            foreach (var t in StyleTinctures(c.Style)) yield return t;
        }

        private static IEnumerable<Tincture> StyleTinctures(Style s)
        {
            if (s == null) yield break;
            yield return s.VoidTincture;
            yield return s.Fimbriation;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
