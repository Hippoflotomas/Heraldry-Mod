using System;
using System.Collections.Generic;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     Where charges go and how big they are. Positions come from heraldic rules (rows, "in pale", "between"
    ///     regions of each ordinary); sizes come from the space actually free there, measured with a coarse
    ///     distance field (distance from each point to the cloth edge or the nearest ordinary). All charges in
    ///     one group share one size, as in real arms.
    /// </summary>
    public sealed partial class FieldRenderer
    {
        /// <summary>
        ///     How rows of field charges use a tall surface. Per surface: banners use Spread (Hippo, 2026-10-13);
        ///     shields may want Compact once they exist.
        /// </summary>
        public enum Arrangement
        {
            /// <summary>Heraldic rows packed together as on a shield, the block centred on the body.</summary>
            Compact,
            /// <summary>Heraldic rows, spread evenly down the whole body. Chosen for banners.</summary>
            Spread,
            /// <summary>Re-flowed into whatever rows make the charges largest ("three" becomes three in pale).</summary>
            Reflow,
        }


        /// <summary>Largest charge, as a fraction of the banner's width (a single charge on its own).</summary>
        public const float MaxCharge = 0.5f;
        /// <summary>How much of its cell a charge fills, leaving a gap to its neighbours.</summary>
        public const float ChargeFill = 0.8f;
        /// <summary>Fimbriation (edging) of ordinaries, fraction of the narrow side.</summary>
        public const float OrdinaryFimbriation = 0.035f;
        /// <summary>Rim of a voided ordinary, fraction of the narrow side.</summary>
        public const float OrdinaryVoidRim = 0.045f;

        private struct Placed
        {
            public Charge C;
            public float X, Y, R; // centre and half size, pixels
            public ChargeArt Art; // picture art; null for geometric charges and the fallback
            public bool Fallback; // no art for this charge (yet): the magenta-and-black roundel
        }

        private static readonly Rgba FallbackMagenta = new Rgba(255, 0, 255);
        private static readonly Rgba FallbackBlack = new Rgba(0, 0, 0);

        /// <summary>Geometric charges need a tincture; art charges need one too unless they're the fallback.</summary>
        private static bool Renderable(Charge c) => c.Tincture != null || !c.Drawable || c.ArtKey != null || c.PackName != null;

        private readonly List<Placed> fieldCharges = new List<Placed>();
        private readonly Dictionary<Ordinary, List<Placed>> onCharges = new Dictionary<Ordinary, List<Placed>>();

        // Coarse grid for layout distances
        private int gs, gw, gh;
        private float[] dFree, dCloth;
        /// <summary>
        ///     Room for charges laid "over all": only the cloth edge and edge ordinaries (chief, bordure) take space;
        ///     a pale or bend through the middle is simply drawn over.
        /// </summary>
        private float[] dOver;
        private readonly Dictionary<Ordinary, float[]> dInside = new Dictionary<Ordinary, float[]>();

        // ================================================================== drawing

        /// <summary>Paints the charges of one list over colour c (what's beneath them).</summary>
        private Rgba DrawCharges(List<Placed> list, Rgba beneath, int fieldRegion, float x, float y)
        {
            Rgba c = beneath;
            foreach (var p in list)
            {
                float reach = p.R * (1.2f + ChargeShapes.RimWidth);
                if (Math.Abs(x - p.X) > reach || Math.Abs(y - p.Y) > reach) continue;
                float u = (x - p.X) / p.R, v = (y - p.Y) / p.R;
                if (p.Fallback)
                {
                    if (ChargeShapes.Fallback(u, v) < 0) c = ChargeShapes.FallbackMagenta(u, v) ? FallbackMagenta : FallbackBlack;
                    continue;
                }
                float d;
                if (p.Art != null)
                {
                    ChargeShapes.Orient(p.C, ref u, ref v);
                    d = p.Art.Distance(u, v, p.C.Mask) * p.R;
                }
                else d = ChargeShapes.Distance(p.C, u, v) * p.R;
                float rim = Math.Max(1.5f, ChargeShapes.RimWidth * p.R);
                var style = p.C.Style;
                if (d >= 0)
                {
                    if (style.Fimbriation != null && d < rim) c = Colour(style.Fimbriation, x, y);
                    continue;
                }
                if (style.Voided && d < -rim)
                {
                    if (style.VoidTincture != null) c = Colour(style.VoidTincture, x, y);
                    continue; // otherwise what's beneath shows through
                }
                if (p.Art == null)
                {
                    c = Tinct(p.C.Tincture, fieldRegion, x, y);
                    continue;
                }
                bool proper = p.C.Tincture == null || p.C.Tincture.Kind == TinctureKind.Proper;
                if (proper)
                {
                    // The art's own colours; a tongue or claws outside the colour image are naturally red
                    c = p.C.Mask != MaskMode.Plain && Math.Max(p.Art.Armed(u, v), p.Art.Langued(u, v)) > p.Art.Body(u, v)
                        ? Lexicon.Palette["gules"] : p.Art.Colour(u, v);
                }
                else c = Tinct(p.C.Tincture, fieldRegion, x, y); // silhouette in the tincture
                if (style.Voided) continue;
                // "armed and langued gules": the mask's claws and tongue in their own tincture
                var mode = p.C.Mask;
                if (p.C.Armed != null && mode == MaskMode.Normal)
                {
                    float arm = p.Art.Armed(u, v);
                    if (arm > 0.01f) c = Mix(c, Colour(p.C.Armed, x, y), arm);
                }
                if (p.C.Langued != null && (mode == MaskMode.Normal || mode == MaskMode.Disarmed))
                {
                    float tongue = p.Art.Langued(u, v);
                    if (tongue > 0.01f) c = Mix(c, Colour(p.C.Langued, x, y), tongue);
                }
                // Interior lines: sable on metals, argent on colours; proper art has its own detail
                float ink = proper || mode == MaskMode.Plain ? 0 : p.Art.Lines(u, v);
                if (ink > 0.01f)
                {
                    var line = Luma(c) < 110 ? Lexicon.Palette["argent"] : Lexicon.Palette["sable"];
                    c = Mix(c, line, ink);
                }
            }
            return c;
        }

        private static int Luma(Rgba c) => (299 * c.R + 587 * c.G + 114 * c.B) / 1000;

        private static Rgba Mix(Rgba a, Rgba b, float t) =>
            new Rgba((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        /// <summary>A tincture at a point; counterchanged takes the field's other tincture there.</summary>
        private Rgba Tinct(Tincture t, int fieldRegion, float x, float y)
        {
            if (t != null && t.Kind == TinctureKind.Counterchanged)
            {
                var f = blazon.Field;
                return f.Tinctures.Count >= 2 ? Colour(f.Tinctures[1 - Math.Min(fieldRegion, 1)], x, y) : Magenta;
            }
            return Colour(t, x, y);
        }

        /// <summary>An ordinary with its styles: fimbriation outside, a voided interior inside.</summary>
        private Rgba DrawOrdinary(Ordinary o, Rgba beneath, int fieldRegion, float x, float y)
        {
            var s = o.Style;
            if (s.Fimbriation != null && !InsideOrdinary(o, x, y, 0) && InsideOrdinary(o, x, y, OrdinaryFimbriation * unit))
                return Colour(s.Fimbriation, x, y);
            if (!InsideOrdinary(o, x, y, 0)) return beneath;
            if (s.Voided && InsideOrdinary(o, x, y, -OrdinaryVoidRim * unit))
                return s.VoidTincture != null ? Colour(s.VoidTincture, x, y) : beneath;
            return Tinct(o.Tincture, fieldRegion, x, y);
        }

        // ================================================================== layout

        private void LayoutCharges()
        {
            bool any = false;
            foreach (var c in blazon.Charges) if (Renderable(c)) any = true;
            foreach (var o in blazon.Ordinaries)
                foreach (var c in o.Charges) if (Renderable(c)) any = true;
            if (!any) return;

            gs = Math.Max(2, (int)Math.Round(unit / 128f));
            gw = (w + gs - 1) / gs;
            gh = (h + gs - 1) / gs;
            dCloth = Chamfer((x, y) => !Cloth(x, y));
            dFree = Chamfer((x, y) =>
            {
                if (!Cloth(x, y)) return true;
                foreach (var o in blazon.Ordinaries)
                    if (o.Tincture != null && InsideOrdinary(o, x, y, o.Style.Fimbriation != null ? OrdinaryFimbriation * unit : 0))
                        return true;
                return false;
            });
            dOver = Chamfer((x, y) =>
            {
                if (!Cloth(x, y)) return true;
                foreach (var o in blazon.Ordinaries)
                    if (o.Tincture != null && (o.Kind == OrdinaryKind.Chief || o.Kind == OrdinaryKind.Bordure) &&
                        InsideOrdinary(o, x, y, o.Style.Fimbriation != null ? OrdinaryFimbriation * unit : 0))
                        return true;
                return false;
            });

            // Charges on ordinaries
            foreach (var o in blazon.Ordinaries)
                foreach (var c in o.Charges)
                    if (Renderable(c))
                        Add(OnOrdinary(o, c), c, onCharges.TryGetValue(o, out var l) ? l : onCharges[o] = new List<Placed>());

            // Field charges: the "between" groups, explicit placements, then the rest share what's left
            float top = 0, bottom = fh;
            var plain = new List<Charge>();
            foreach (var c in blazon.Charges)
            {
                if (!Renderable(c)) continue;
                if (c.Placement == Placement.Between && c.Host != null) Add(Between(c.Host, c), c, fieldCharges);
                else if (c.Placement == Placement.None) plain.Add(c);
                else
                {
                    var eg = Explicit(c);
                    if (eg.Space == null) eg.Space = dOver;
                    Add(eg, c, fieldCharges);
                    if (c.Placement == Placement.Chief) top = Math.Max(top, ChiefDepth * fh);
                    if (c.Placement == Placement.Base) bottom = Math.Min(bottom, (1 - ChiefDepth) * fh);
                }
            }
            if (plain.Count == 0) return;

            // Free box: where the field isn't covered (below a chief, inside a bordure), within the body
            FreeBox(ref top, ref bottom, out float left, out float right);
            int totalRows = 0;
            var rowsOf = new List<int[]>();
            foreach (var c in plain)
            {
                var rows = c.Rows ?? (arrangement == Arrangement.Reflow ? ReflowRows(c.Count, right - left, (bottom - top) / plain.Count) : DefaultRows(c.Count));
                rowsOf.Add(rows);
                totalRows += rows.Length;
            }
            // Several groups ("a mullet and two roundels") stack down the box, each in its share of the rows
            float y0 = top;
            for (int i = 0; i < plain.Count; i++)
            {
                float y1 = y0 + (bottom - top) * rowsOf[i].Length / totalRows;
                var pg = RowsInBox(rowsOf[i], left, y0, right, y1, arrangement != Arrangement.Compact);
                pg.Space = dOver; // charges with no placement lie over any ordinary crossing the field
                Add(pg, plain[i], fieldCharges);
                y0 = y1;
            }
        }

        /// <summary>A group: positions plus the size they allow. Shrinks to fit the free space at every position.</summary>
        private struct Group
        {
            public List<float> Xs, Ys;
            public float R;
            public float[] Space; // which distance field limits the size (null = free field)

            public static Group Make(float r) => new Group { Xs = new List<float>(), Ys = new List<float>(), R = r };
            public void Put(float x, float y) { Xs.Add(x); Ys.Add(y); }
            public int Count => Xs.Count;
        }

        private void Add(Group g, Charge c, List<Placed> into)
        {
            if (g.Xs == null || g.Count == 0) return;
            float r = Math.Min(g.R, MaxCharge * w * 0.5f);
            var space = g.Space ?? dFree;
            for (int i = 0; i < g.Count; i++)
                r = Math.Min(r, Grid(space, g.Xs[i], g.Ys[i]) * 0.94f);
            r = Math.Max(r, 2f);
            var art = c.ArtKey != null ? ChargeArtLibrary.Get(c.ArtKey) : null;
            bool fallback = !c.Drawable || ((c.ArtKey != null || c.PackName != null) && art == null);
            for (int i = 0; i < g.Count; i++)
                into.Add(new Placed { C = c, X = g.Xs[i], Y = g.Ys[i], R = r, Art = art, Fallback = fallback });
        }

        // ------------------------------------------------------------------ rows

        /// <summary>Heraldic default rows for a count (on a shield): 3 = two and one, 6 = three, two and one.</summary>
        private static int[] DefaultRows(int n)
        {
            switch (n)
            {
                case 1: return new[] { 1 };
                case 2: return new[] { 1, 1 };          // two are in pale unless blazoned otherwise
                case 3: return new[] { 2, 1 };
                case 4: return new[] { 2, 2 };
                case 5: return new[] { 2, 1, 2 };       // in saltire
                case 6: return new[] { 3, 2, 1 };
                case 7: return new[] { 3, 3, 1 };
                case 8: return new[] { 3, 2, 3 };
                case 9: return new[] { 3, 3, 3 };
                case 10: return new[] { 4, 3, 2, 1 };
            }
            var rows = new List<int>();
            for (int left = n; left > 0; left -= 3) rows.Add(Math.Min(3, left));
            return rows.ToArray();
        }

        /// <summary>The rows that make n charges largest in a box of this shape (larger rows first).</summary>
        private static int[] ReflowRows(int n, float bw, float bh)
        {
            int[] best = null;
            float bestCell = -1;
            for (int r = 1; r <= n; r++)
            {
                int q = n / r, e = n % r;
                int widest = q + (e > 0 ? 1 : 0);
                float cell = Math.Min(bw / widest, bh / r);
                if (cell <= bestCell + 0.5f) continue;
                bestCell = cell;
                best = new int[r];
                // Odd row count with one short row: put the short one in the middle (2-1-2)
                int shortRows = r - e;
                for (int i = 0; i < r; i++) best[i] = q + (i < e ? 1 : 0);
                if (e > 0 && shortRows == 1 && r % 2 == 1)
                {
                    for (int i = 0; i < r; i++) best[i] = q + 1;
                    best[r / 2] = q;
                }
            }
            return best;
        }

        /// <summary>Rows centred in a box, all cells the same width so short rows nest between long ones.</summary>
        private Group RowsInBox(int[] rows, float x0, float y0, float x1, float y1, bool spread)
        {
            int maxRow = 1;
            foreach (int k in rows) maxRow = Math.Max(maxRow, k);
            float bw = x1 - x0, bh = y1 - y0;
            float px = bw / maxRow;
            float py = spread ? bh / rows.Length : Math.Min(px, bh / rows.Length);
            var g = Group.Make(Math.Min(px, py) * 0.5f * ChargeFill);
            float top = y0 + (bh - py * rows.Length) * 0.5f, cx = (x0 + x1) * 0.5f;
            for (int i = 0; i < rows.Length; i++)
                for (int j = 0; j < rows[i]; j++)
                    g.Put(cx + (j - (rows[i] - 1) * 0.5f) * px, top + (i + 0.5f) * py);
            return g;
        }

        /// <summary>The part of the body not covered at its top, bottom and sides (by a chief, bordure...).</summary>
        private void FreeBox(ref float top, ref float bottom, out float left, out float right)
        {
            float min = 0.04f * unit;
            int gx0 = gw, gx1 = -1, gy0 = gh, gy1 = -1;
            for (int gy = 0; gy < gh; gy++)
            {
                float y = (gy + 0.5f) * gs;
                if (y < top || y > bottom) continue;
                for (int gx = 0; gx < gw; gx++)
                {
                    if (dOver[gy * gw + gx] < min) continue;
                    gx0 = Math.Min(gx0, gx); gx1 = Math.Max(gx1, gx);
                    gy0 = Math.Min(gy0, gy); gy1 = Math.Max(gy1, gy);
                }
            }
            if (gx1 < 0) { left = 0; right = w; return; }
            // Back out by the margin we required, so a plain field uses its full width
            left = Math.Max(0, gx0 * gs - min);
            right = Math.Min(w, (gx1 + 1) * gs + min);
            top = Math.Max(top, gy0 * gs - min);
            bottom = Math.Min(bottom, (gy1 + 1) * gs + min);
        }

        // ------------------------------------------------------------------ explicit placements

        private Group Explicit(Charge c)
        {
            int n = c.Count;
            float cx = w * 0.5f, cy = fh * 0.5f;
            switch (c.Placement)
            {
                case Placement.Pale:
                    return RowsInBox(Repeat(1, n), 0, 0, w, fh, true);
                case Placement.Fess:
                {
                    float half = Math.Min(w / n, fh * 0.3f) * 0.5f;
                    return RowsInBox(new[] { n }, 0, cy - half, w, cy + half, false);
                }
                case Placement.Chief:
                    return RowsInBox(n <= 4 ? new[] { n } : DefaultRows(n), 0, 0, w, ChiefDepth * fh, false);
                case Placement.Base:
                    return RowsInBox(n <= 4 ? new[] { n } : DefaultRows(n), 0, (1 - ChiefDepth) * fh, w, fh, false);
                case Placement.Bend:
                case Placement.BendSinister:
                {
                    float dx = c.Placement == Placement.Bend ? w : -w, dy = fh;
                    return Line(cx, cy, dx / diag, dy / diag, n, dOver, null);
                }
                case Placement.Cross:
                    return Arms(cx, cy, new[] { 0f, 0f, -1f, 1f }, new[] { -1f, 1f, 0f, 0f }, n, dOver);
                case Placement.Saltire:
                {
                    float ux = w / diag, uy = fh / diag;
                    return Arms(cx, cy, new[] { -ux, ux, -ux, ux }, new[] { -uy, -uy, uy, uy }, n, dOver);
                }
                case Placement.Orle:
                    return Around(dOver, 0.16f * unit, n, 0.16f * unit * 0.85f);
                case Placement.DexterChief: case Placement.MiddleChief: case Placement.SinisterChief:
                case Placement.DexterFlank: case Placement.SinisterFlank:
                case Placement.DexterBase: case Placement.MiddleBase: case Placement.SinisterBase:
                case Placement.HonourPoint: case Placement.FessPoint: case Placement.NombrilPoint:
                    return AtPoint(c.Placement, n);
            }
            return RowsInBox(DefaultRows(n), 0, 0, w, fh, true);
        }

        /// <summary>
        ///     The points of the shield: the free field (inside a bordure, below a chief) split into thirds each
        ///     way; the honour and nombril points sit halfway between the fess point and the chief or base.
        /// </summary>
        private Group AtPoint(Placement p, int n)
        {
            float top = 0, bottom = fh;
            FreeBox(ref top, ref bottom, out float left, out float right);
            float cw = (right - left) / 3f, rh = (bottom - top) / 3f;
            int col = 1, row = 1;
            float rowsOffset = 0f;
            switch (p)
            {
                case Placement.DexterChief: col = 0; row = 0; break;
                case Placement.MiddleChief: row = 0; break;
                case Placement.SinisterChief: col = 2; row = 0; break;
                case Placement.DexterFlank: col = 0; break;
                case Placement.SinisterFlank: col = 2; break;
                case Placement.DexterBase: col = 0; row = 2; break;
                case Placement.MiddleBase: row = 2; break;
                case Placement.SinisterBase: col = 2; row = 2; break;
                case Placement.HonourPoint: rowsOffset = -0.5f; break;
                case Placement.NombrilPoint: rowsOffset = 0.5f; break;
            }
            float x0 = left + col * cw, y0 = top + (row + rowsOffset) * rh;
            var rows = n <= 3 ? new[] { n } : DefaultRows(n);
            return RowsInBox(rows, x0, y0, x0 + cw, y0 + rh, false);
        }

        private static int[] Repeat(int v, int n)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = v;
            return a;
        }

        // ------------------------------------------------------------------ on an ordinary

        private Group OnOrdinary(Ordinary o, Charge c)
        {
            int n = c.Count;
            var inside = InsideField(o);
            float hw = (o.Diminutive ? DiminutiveWidth : BandWidth) * unit * 0.5f;
            float cx = w * 0.5f, cy = fh * 0.5f;
            Group g;
            switch (o.Kind)
            {
                case OrdinaryKind.Chief:
                    g = Line(cx, ChiefDepth * fh * 0.5f, 1, 0, n, inside, null);
                    break;
                case OrdinaryKind.Fess:
                    g = Line(cx, o.Count == 1 ? cy : fh / (o.Count + 1f), 1, 0, n, inside, null);
                    break;
                case OrdinaryKind.Pale:
                    g = Line(o.Count == 1 ? cx : w / (o.Count + 1f), cy, 0, 1, n, inside, null);
                    break;
                case OrdinaryKind.Bend:
                case OrdinaryKind.BendSinister:
                {
                    float dx = o.Kind == OrdinaryKind.Bend ? w : -w;
                    g = Line(cx, cy, dx / diag, fh / diag, n, inside, null);
                    break;
                }
                case OrdinaryKind.Cross:
                    g = Arms(cx, cy, new[] { 0f, 0f, -1f, 1f }, new[] { -1f, 1f, 0f, 0f }, n, inside);
                    break;
                case OrdinaryKind.Saltire:
                {
                    float ux = w / diag, uy = fh / diag;
                    g = Arms(cx, cy, new[] { -ux, ux, -ux, ux }, new[] { -uy, -uy, uy, uy }, n, inside);
                    break;
                }
                case OrdinaryKind.Chevron:
                {
                    float ay = ChevronApex * fh, len = (float)Math.Sqrt(cx * cx + (fh - ay) * (fh - ay));
                    float lx = cx / len, ly = (fh - ay) / len;
                    // Spread down the legs rather than bunched at the point
                    g = Arms(cx, ay, new[] { -lx, lx }, new[] { ly, ly }, n, inside, new[] { len * 0.9f, len * 0.9f });
                    break;
                }
                case OrdinaryKind.Pall:
                {
                    float py = fh * 0.42f, l = (float)Math.Sqrt(cx * cx + py * py);
                    g = Arms(cx, py, new[] { -cx / l, cx / l, 0f }, new[] { -py / l, -py / l, 1f }, n, inside,
                        new[] { l * 0.9f, l * 0.9f, (fh - py) * 0.9f });
                    break;
                }
                case OrdinaryKind.Bordure:
                {
                    float bw = BordureWidth * unit;
                    g = Around(dCloth, bw * 0.5f, n, bw * 0.5f * 0.88f);
                    break;
                }
                case OrdinaryKind.Pile:
                {
                    Pole(inside, (x, y) => true, out float px, out float py, out _);
                    g = Line(px, py, 0, 1, n, inside, null);
                    break;
                }
                default:
                    g = Group.Make(hw);
                    g.Put(cx, cy);
                    break;
            }
            g.Space = inside;
            return g;
        }

        // ------------------------------------------------------------------ between an ordinary's limbs

        private Group Between(Ordinary o, Charge c)
        {
            int n = c.Count;
            float hw = (o.Diminutive ? DiminutiveWidth : BandWidth) * unit * 0.5f;
            var parts = new List<Group>();
            switch (o.Kind)
            {
                case OrdinaryKind.Fess:
                {
                    int bars = Math.Max(1, o.Count);
                    float first = bars == 1 ? fh * 0.5f : fh / (bars + 1f), last = bars == 1 ? fh * 0.5f : fh * bars / (bars + 1f);
                    int up = (n + 1) / 2, down = n - up;
                    parts.Add(RowsInBox(up <= 3 ? new[] { up } : DefaultRows(up), 0, 0, w, first - hw, false));
                    if (down > 0)
                        parts.Add(RowsInBox(down == 3 ? new[] { 2, 1 } : down <= 3 ? new[] { down } : DefaultRows(down), 0, last + hw, w, fh, false));
                    break;
                }
                case OrdinaryKind.Pale:
                {
                    int left = (n + 1) / 2, right = n - left;
                    parts.Add(RowsInBox(Repeat(1, left), 0, 0, w * 0.5f - hw, fh, true));
                    if (right > 0) parts.Add(RowsInBox(Repeat(1, right), w * 0.5f + hw, 0, w, fh, true));
                    break;
                }
                case OrdinaryKind.Cross:
                {
                    // Round the quarters: 1st (dexter chief), 2nd, 3rd, 4th
                    var counts = Share(n, 4);
                    float cx = w * 0.5f, cy = fh * 0.5f;
                    float[][] boxes =
                    {
                        new[] { 0f, 0f, cx - hw, cy - hw }, new[] { cx + hw, 0f, (float)w, cy - hw },
                        new[] { 0f, cy + hw, cx - hw, fh }, new[] { cx + hw, cy + hw, (float)w, fh },
                    };
                    for (int i = 0; i < 4; i++)
                        if (counts[i] > 0)
                            parts.Add(RowsInBox(DefaultRows(counts[i]), boxes[i][0], boxes[i][1], boxes[i][2], boxes[i][3], false));
                    break;
                }
                case OrdinaryKind.Saltire:
                {
                    // Chief, base, then the flanks
                    var counts = Share(n, 4);
                    Func<float, float, bool>[] regions =
                    {
                        (x, y) => SalA(x, y) && SalB(x, y), (x, y) => !SalA(x, y) && !SalB(x, y),
                        (x, y) => !SalA(x, y) && SalB(x, y), (x, y) => SalA(x, y) && !SalB(x, y),
                    };
                    for (int i = 0; i < 4; i++)
                        if (counts[i] > 0) parts.Add(InRegion(regions[i], counts[i], i < 2 ? 1f : 0f, i < 2 ? 0f : 1f));
                    break;
                }
                case OrdinaryKind.Bend:
                case OrdinaryKind.BendSinister:
                {
                    bool sinister = o.Kind == OrdinaryKind.BendSinister;
                    float dx = (sinister ? -w : w) / diag, dy = fh / diag;
                    int above = (n + 1) / 2;
                    parts.Add(InRegion((x, y) => BendSide(x, y, sinister) > 0, above, dx, dy));
                    if (n - above > 0) parts.Add(InRegion((x, y) => BendSide(x, y, sinister) < 0, n - above, dx, dy));
                    break;
                }
                case OrdinaryKind.Chevron:
                {
                    // Two in chief either side of the point and one in base, extended in that proportion
                    int below = n == 1 ? 1 : n == 2 ? 0 : Math.Max(1, n / 3);
                    int left = (n - below + 1) / 2, right = n - below - left;
                    float cx = w * 0.5f, ay = ChevronApex * fh, len = (float)Math.Sqrt(cx * cx + (fh - ay) * (fh - ay));
                    float lx = cx / len, ly = (fh - ay) / len;
                    if (left > 0) parts.Add(InRegion((x, y) => ChevronSide(x, y, 0, LineKind.Straight) < 0 && x < cx, left, -lx, ly));
                    if (right > 0) parts.Add(InRegion((x, y) => ChevronSide(x, y, 0, LineKind.Straight) < 0 && x >= cx, right, lx, ly));
                    if (below > 0) parts.Add(InRegion((x, y) => ChevronSide(x, y, 0, LineKind.Straight) > 0, below, 1, 0));
                    break;
                }
                case OrdinaryKind.Pile:
                {
                    int left = (n + 1) / 2;
                    parts.Add(InRegion((x, y) => x < w * 0.5f, left, 0, 1));
                    if (n - left > 0) parts.Add(InRegion((x, y) => x >= w * 0.5f, n - left, 0, 1));
                    break;
                }
                case OrdinaryKind.Pall:
                {
                    float cx = w * 0.5f, py = fh * 0.42f;
                    Func<float, float, bool> topPart = (x, y) => y < py - Math.Abs(x - cx) * py / cx;
                    var counts = Share(n, 3);
                    if (counts[0] > 0) parts.Add(InRegion(topPart, counts[0], 1, 0));
                    if (counts[1] > 0) parts.Add(InRegion((x, y) => x < cx && !topPart(x, y), counts[1], 0, 1));
                    if (counts[2] > 0) parts.Add(InRegion((x, y) => x >= cx && !topPart(x, y), counts[2], 0, 1));
                    break;
                }
                default:
                    return RowsInBox(DefaultRows(n), 0, 0, w, fh, true);
            }

            // One group, one size: the smallest any part allows
            var all = Group.Make(float.MaxValue);
            foreach (var p in parts)
            {
                if (p.Xs == null) continue;
                float r = p.R;
                for (int i = 0; i < p.Count; i++) r = Math.Min(r, Grid(dFree, p.Xs[i], p.Ys[i]) * 0.94f);
                all.R = Math.Min(all.R, r);
                for (int i = 0; i < p.Count; i++) all.Put(p.Xs[i], p.Ys[i]);
            }
            return all;
        }

        private bool SalA(float x, float y) => (x * fh - y * w) / diag > 0;
        private bool SalB(float x, float y) => ((w - x) * fh - y * w) / diag > 0;
        private float BendSide(float x, float y, bool sinister) => ((sinister ? w - x : x) * fh - y * w) / diag;

        /// <summary>Spreads n over k places, earlier places first (5 over 4 = 2,1,1,1).</summary>
        private static int[] Share(int n, int k)
        {
            var a = new int[k];
            for (int i = 0; i < n; i++) a[i % k]++;
            return a;
        }

        /// <summary>k charges in a region: at its roomiest point, in a line along (dx, dy) when there are several.</summary>
        private Group InRegion(Func<float, float, bool> region, int k, float dx, float dy)
        {
            Pole(dFree, region, out float px, out float py, out _);
            return Line(px, py, dx, dy, k, dFree, region);
        }

        // ------------------------------------------------------------------ geometric helpers

        /// <summary>
        ///     k charges evenly along a line through (cx, cy): the largest size where every one still has room
        ///     in the given distance field (and stays in the region, if one is given).
        /// </summary>
        private Group Line(float cx, float cy, float dx, float dy, int k, float[] space, Func<float, float, bool> region)
        {
            float r = Math.Min(MaxCharge * w * 0.5f, Math.Max(2f, Grid(space, cx, cy)));
            for (int iter = 0; iter < 60; iter++, r *= 0.94f)
            {
                float pitch = 2 * r / ChargeFill;
                bool ok = true;
                for (int j = 0; j < k && ok; j++)
                {
                    float t = (j - (k - 1) * 0.5f) * pitch;
                    float x = cx + dx * t, y = cy + dy * t;
                    if (Grid(space, x, y) < r || (region != null && !region(x, y))) ok = false;
                }
                if (ok || r < 2f) break;
            }
            var g = Group.Make(r);
            float p2 = 2 * r / ChargeFill;
            for (int j = 0; j < k; j++)
            {
                float t = (j - (k - 1) * 0.5f) * p2;
                g.Put(cx + dx * t, cy + dy * t);
            }
            return g;
        }

        /// <summary>
        ///     n charges on arms out from a centre (a cross, saltire, chevron or pall): one in the middle when n is
        ///     odd, the rest shared round the arms in order. Packed close to the centre, or spread evenly along
        ///     each arm when its length is given.
        /// </summary>
        private Group Arms(float cx, float cy, float[] dxs, float[] dys, int n, float[] space, float[] armLengths = null)
        {
            int arms = dxs.Length;
            bool centre = arms == 3 ? n % 3 == 1 : n % 2 == 1; // a pall takes three as one per arm
            var perArm = Share(centre ? n - 1 : n, arms);
            float r = Math.Min(MaxCharge * w * 0.5f, Math.Max(2f, Grid(space, cx, cy)));
            if (armLengths != null)
                for (int a = 0; a < arms; a++)
                    if (perArm[a] > 0) r = Math.Min(r, armLengths[a] / (perArm[a] + 0.6f) * 0.5f * ChargeFill);
            for (int iter = 0; iter < 60; iter++, r *= 0.94f)
            {
                if (Fits(r)) break;
                if (r < 2f) break;
            }
            var g = Group.Make(r);
            Emit(r, (x, y) => g.Put(x, y));
            return g;

            bool Fits(float rr)
            {
                bool ok = true;
                Emit(rr, (x, y) => { if (Grid(space, x, y) < rr) ok = false; });
                return ok;
            }

            void Emit(float rr, Action<float, float> put)
            {
                float pitch = 2 * rr / ChargeFill;
                if (centre) put(cx, cy);
                for (int a = 0; a < arms; a++)
                    for (int j = 1; j <= perArm[a]; j++)
                    {
                        // Without a centre charge the first ring sits close in, but far enough that neighbours on
                        // perpendicular arms don't touch
                        float dist = armLengths != null ? armLengths[a] * j / (perArm[a] + 0.6f)
                            : centre ? j * pitch : (j - 0.3f) * pitch;
                        put(cx + dxs[a] * dist, cy + dys[a] * dist);
                    }
            }
        }

        /// <summary>
        ///     n charges evenly round the field at a given distance in from the edge (an orle, or on a bordure),
        ///     starting from the top centre.
        /// </summary>
        private Group Around(float[] space, float inset, int n, float maxR)
        {
            const int Steps = 720;
            float cx = w * 0.5f, cy = Math.Min(h * 0.5f, fh * 0.55f);
            var xs = new float[Steps + 1];
            var ys = new float[Steps + 1];
            var arc = new float[Steps + 1];
            float far = (float)Math.Sqrt((double)w * w + (double)h * h);
            for (int i = 0; i <= Steps; i++)
            {
                double a = 2 * Math.PI * i / Steps; // 0 = up, clockwise
                float dx = (float)Math.Sin(a), dy = -(float)Math.Cos(a);
                // March outward to the last point that is still at least 'inset' in from the edge
                float t = 0, step = gs * 0.5f;
                while (t < far && Grid(space, cx + dx * (t + step), cy + dy * (t + step)) >= inset) t += step;
                xs[i] = cx + dx * t;
                ys[i] = cy + dy * t;
                if (i > 0)
                    arc[i] = arc[i - 1] + (float)Math.Sqrt((xs[i] - xs[i - 1]) * (xs[i] - xs[i - 1]) + (ys[i] - ys[i - 1]) * (ys[i] - ys[i - 1]));
            }
            float total = arc[Steps], spacing = total / n;
            var g = Group.Make(Math.Min(maxR, spacing * 0.5f * ChargeFill));
            int k = 0;
            for (int j = 0; j < n; j++)
            {
                float target = (j + 0.5f) * spacing;
                while (k < Steps && arc[k + 1] < target) k++;
                float f = arc[k + 1] > arc[k] ? (target - arc[k]) / (arc[k + 1] - arc[k]) : 0;
                g.Put(xs[k] + (xs[k + 1] - xs[k]) * f, ys[k] + (ys[k + 1] - ys[k]) * f);
            }
            g.Space = space;
            return g;
        }

        /// <summary>The roomiest point of a region (largest distance to anything).</summary>
        private void Pole(float[] space, Func<float, float, bool> region, out float px, out float py, out float best)
        {
            best = -1;
            px = w * 0.5f;
            py = fh * 0.5f;
            for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                float d = space[gy * gw + gx];
                if (d <= best) continue;
                float x = (gx + 0.5f) * gs, y = (gy + 0.5f) * gs;
                if (!region(x, y)) continue;
                best = d;
                px = x;
                py = y;
            }
        }

        /// <summary>Distance field inside one ordinary (for sizing the charges on it).</summary>
        private float[] InsideField(Ordinary o)
        {
            if (!dInside.TryGetValue(o, out var f))
                dInside[o] = f = Chamfer((x, y) => !Cloth(x, y) || !InsideOrdinary(o, x, y, 0));
            return f;
        }

        private bool Cloth(float x, float y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return false;
            return outline == null || outline[(int)y * w + (int)x];
        }

        private float Grid(float[] field, float x, float y)
        {
            int gx = (int)(x / gs), gy = (int)(y / gs);
            if (gx < 0 || gy < 0 || gx >= gw || gy >= gh) return 0;
            return field[gy * gw + gx];
        }

        /// <summary>Two-pass chamfer distance (pixels) on the coarse grid from every blocked cell.</summary>
        private float[] Chamfer(Func<float, float, bool> blocked)
        {
            float A = gs, D = gs * 1.4142f;
            var d = new float[gw * gh];
            for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                float x = (gx + 0.5f) * gs, y = (gy + 0.5f) * gs;
                d[gy * gw + gx] = blocked(x, y) ? 0 : Math.Min(Math.Min(x, w - x), Math.Min(y, h - y));
            }
            for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                float v = d[gy * gw + gx];
                if (gx > 0) v = Math.Min(v, d[gy * gw + gx - 1] + A);
                if (gy > 0)
                {
                    v = Math.Min(v, d[(gy - 1) * gw + gx] + A);
                    if (gx > 0) v = Math.Min(v, d[(gy - 1) * gw + gx - 1] + D);
                    if (gx < gw - 1) v = Math.Min(v, d[(gy - 1) * gw + gx + 1] + D);
                }
                d[gy * gw + gx] = v;
            }
            for (int gy = gh - 1; gy >= 0; gy--)
            for (int gx = gw - 1; gx >= 0; gx--)
            {
                float v = d[gy * gw + gx];
                if (gx < gw - 1) v = Math.Min(v, d[gy * gw + gx + 1] + A);
                if (gy < gh - 1)
                {
                    v = Math.Min(v, d[(gy + 1) * gw + gx] + A);
                    if (gx < gw - 1) v = Math.Min(v, d[(gy + 1) * gw + gx + 1] + D);
                    if (gx > 0) v = Math.Min(v, d[(gy + 1) * gw + gx - 1] + D);
                }
                d[gy * gw + gx] = v;
            }
            return d;
        }
    }
}
