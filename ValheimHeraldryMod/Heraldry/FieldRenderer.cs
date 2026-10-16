using System;
using System.Collections.Generic;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     Draws the field and ordinaries of a parsed blazon into an RGBA pixel array (rows top to bottom).
    ///     Geometry follows the "fill the banner" decision: positions and directions come from the banner's own
    ///     rectangle (a bend runs corner to corner), while widths and line patterns use one pixel scale (based on
    ///     the narrow side) so bands look even and wavy/engrailed edges aren't stretched.
    ///     Pure and deterministic: same text and size give the same pixels on every client.
    /// </summary>
    public sealed partial class FieldRenderer
    {
        // ---- Tunable proportions (fractions of the narrow side unless noted) ----
        public const float BandWidth = 0.25f;       // pale, fess, bend, cross, saltire, chevron, pall
        public const float DiminutiveWidth = 0.13f; // bars, pallets, bendlets, chevronels, plurals
        public const float ChiefDepth = 0.25f;      // fraction of the HEIGHT
        public const float BordureWidth = 0.10f;
        public const float ChevronApex = 0.35f;     // fraction of the height
        /// <summary>
        ///     Tartan threads across the narrow side. Like the furs, a tartan is sized to the object it's on, so a
        ///     typical sett (about 300 threads) shows about one and a half repeats across a banner's width.
        /// </summary>
        public const float TartanThreadsAcross = 450f;
        private const int Samples = 3;              // 3x3 supersampling at edges only

        private static readonly Rgba Magenta = new Rgba(255, 0, 255);
        private static readonly Rgba Black = new Rgba(0, 0, 0);

        private readonly Blazon blazon;
        private readonly int w, h;
        private readonly float unit; // narrow side, in pixels
        /// <summary>
        ///     Height of the field's body: down to the shoulders where a pointed or swallowtailed bottom starts
        ///     (the full height for a plain rectangle). Centre, diagonals and divisions are laid out on this body,
        ///     so a bend ends at the shoulder instead of running into a corner the point has cut away.
        /// </summary>
        private readonly float fh;
        private readonly float diag; // length of the body's corner-to-corner diagonal

        /// <summary>Optional mask of the cloth's real outline (true = cloth), rows top to bottom.</summary>
        private readonly bool[] outline;
        private readonly Arrangement arrangement;
        private float[] edgeDistance; // distance to the outline edge, for bordures

        /// <summary>
        ///     <paramref name="outline"/> is the banner's real cloth shape (e.g. the pointed bottom of the vanilla
        ///     banner) at the same size; null means the whole rectangle.
        ///     <paramref name="arrangement"/>: how charges without a placement use the surface (banners: Spread).
        /// </summary>
        /// <param name="centreU">
        ///     Where the field's centre (fess point) lies, as fractions of the width and height from the top left;
        ///     negative = the middle of the field (2026-10-14: lopsided shapes such as the serpent scale, and the
        ///     ValheimHeraldryMod_CentrePoint tag on modders' faces). Partition lines, ordinaries and charges at the
        ///     fess point meet there.
        /// </param>
        public static Rgba[] Render(Blazon blazon, int width, int height, bool[] outline = null,
            Arrangement arrangement = Arrangement.Spread, float centreU = -1f, float centreV = -1f)
        {
            // Rows hidden at the top (behind the beam, empty in the outline): lay the arms out on the visible part
            // only, so a chief or bordure starts where the cloth can be seen, then carry the top row up into them.
            int hidden = HiddenRows(outline, width, height);
            if (hidden == 0)
                return new FieldRenderer(blazon, width, height, outline, arrangement, centreU, centreV).Run();
            int vh = height - hidden;
            var visibleOutline = new bool[width * vh];
            Array.Copy(outline, hidden * width, visibleOutline, 0, visibleOutline.Length);
            float cv = centreV < 0 ? centreV : (centreV * height - hidden) / vh;
            var visible = new FieldRenderer(blazon, width, vh, visibleOutline, arrangement, centreU, cv).Run();
            var px = new Rgba[width * height];
            for (int y = 0; y < hidden; y++) Array.Copy(visible, 0, px, y * width, width);
            Array.Copy(visible, 0, px, hidden * width, visible.Length);
            return px;
        }

        /// <summary>Leading rows with (almost) no cloth in the outline; 0 if there's no outline.</summary>
        private static int HiddenRows(bool[] outline, int w, int h)
        {
            if (outline == null || outline.Length != w * h) return 0;
            for (int y = 0; y < h / 3; y++)
            {
                int covered = 0;
                for (int x = 0; x < w; x++) if (outline[y * w + x]) covered++;
                if (covered > w * 0.02f) return y;
            }
            return 0;
        }

        private FieldRenderer(Blazon b, int width, int height, bool[] outline, Arrangement arrangement, float centreU, float centreV)
        {
            this.arrangement = arrangement;
            blazon = b;
            w = width;
            h = height;
            this.outline = outline != null && outline.Length == width * height ? outline : null;
            unit = Math.Min(width, height);
            fh = BodyHeight();
            diag = (float)Math.Sqrt((double)width * width + (double)fh * fh);
            // The centre: given, or the middle of the body. Kept well inside the field so the warp stays sane.
            cxp = centreU >= 0 ? Math.Max(0.15f, Math.Min(0.85f, centreU)) * w : w * 0.5f;
            cyp = centreV >= 0 ? Math.Max(0.15f, Math.Min(0.85f, centreV)) * h : fh * 0.5f;
        }

        /// <summary>The field's centre in pixels (the fess point).</summary>
        private readonly float cxp, cyp;

        /// <summary>
        ///     From the real field to the standard one whose centre is the middle: piecewise linear on each side of
        ///     the centre point, so lines drawn through the middle of the standard field pass through the centre
        ///     point. Identity when the centre is the middle.
        /// </summary>
        private float WX(float x) => x < cxp ? x * (w * 0.5f) / cxp : w * 0.5f + (x - cxp) * (w * 0.5f) / Math.Max(1f, w - cxp);
        private float WY(float y) => y < cyp ? y * (fh * 0.5f) / cyp : fh * 0.5f + (y - cyp) * (fh * 0.5f) / Math.Max(1f, fh - cyp);

        private Rgba[] Run()
        {
            var px = new Rgba[w * h];
            if (blazon == null || blazon.Field == null || blazon.Field.Tinctures.Count == 0)
            {
                // Nothing drawable: the magenta-and-black "missing" quarters
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = (x < w / 2) == (y < h / 2) ? Magenta : Black;
                return px;
            }

            foreach (var o in blazon.Ordinaries)
                if (o.Kind == OrdinaryKind.Bordure) { edgeDistance = DistanceToEdge(); break; }
            LayoutCharges();

            // Pass 1: one sample per pixel centre
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = Sample(x + 0.5f, y + 0.5f);

            // A tartan differs from pixel to pixel everywhere, and it's already filtered: find the edges on a copy
            // with every tartan drawn flat, so only real edges (ordinaries, charges) are supersampled
            var edges = px;
            if (tartans.Count > 0)
            {
                edges = new Rgba[w * h];
                flatTartans = true;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    edges[y * w + x] = Sample(x + 0.5f, y + 0.5f);
                flatTartans = false;
            }

            // Pass 2: supersample only where a neighbour differs (edges); flat areas stay as they are
            var result = (Rgba[])px.Clone();
            float step = 1f / Samples;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c0 = edges[y * w + x];
                if ((x == 0 || Same(c0, edges[y * w + x - 1])) && (x == w - 1 || Same(c0, edges[y * w + x + 1])) &&
                    (y == 0 || Same(c0, edges[(y - 1) * w + x])) && (y == h - 1 || Same(c0, edges[(y + 1) * w + x])))
                    continue;
                int r = 0, g = 0, b = 0;
                for (int sy = 0; sy < Samples; sy++)
                for (int sx = 0; sx < Samples; sx++)
                {
                    var c = Sample(x + (sx + 0.5f) * step, y + (sy + 0.5f) * step);
                    r += c.R; g += c.G; b += c.B;
                }
                int n = Samples * Samples;
                result[y * w + x] = new Rgba((byte)(r / n), (byte)(g / n), (byte)(b / n));
            }
            if (blazon.Tattered > 0) Tatter(result);
            return result;
        }

        /// <summary>First row (from the top, below a third of the height) where the cloth stops filling the width.</summary>
        private float BodyHeight()
        {
            if (outline == null) return h;
            // A tabard panel: its sides wander a little with the body, but it's all field
            if (arrangement == Arrangement.Chest) return h;
            // A round or already-narrowing shape (a round shield) has no straight body to lay out on: use it all
            int third = 0;
            for (int x = 0; x < w; x++) if (outline[(h / 3) * w + x]) third++;
            if (third < w * 0.98f) return h;
            for (int y = h / 3; y < h; y++)
            {
                int covered = 0;
                for (int x = 0; x < w; x++) if (outline[y * w + x]) covered++;
                if (covered < w * 0.98f)
                {
                    // A shield that tapers for most of its height (a heater) is laid out on all of it, like real
                    // heraldry; only a long straight body (a banner, a tower shield) ends at its shoulders
                    if (arrangement == Arrangement.Compact && y < h * 0.6f) return h;
                    return y;
                }
            }
            return h;
        }

        private static bool Same(Rgba a, Rgba b) => a.R == b.R && a.G == b.G && a.B == b.B;

        private Rgba Sample(float x, float y)
        {
            var field = blazon.Field;
            int region = FieldRegion(field, x, y);
            Rgba c = Colour(field.Tinctures[Math.Min(region, field.Tinctures.Count - 1)], x, y);

            foreach (var o in blazon.Ordinaries)
            {
                if (o.Tincture == null) continue;
                c = o.Style.IsPlain
                    ? (InsideOrdinary(o, x, y, 0) ? Tinct(o.Tincture, region, x, y) : c)
                    : DrawOrdinary(o, c, region, x, y);
                if (o.Inscription != null && InsideOrdinary(o, x, y, 0)) c = OrdinaryText(o, c, region, x, y);
                if (onCharges.TryGetValue(o, out var on)) c = DrawCharges(on, c, region, x, y);
            }
            if (fieldCharges.Count > 0) c = DrawCharges(fieldCharges, c, region, x, y);
            return c;
        }

        // ================================================================== field

        /// <summary>0 = first tincture, 1 = second.</summary>
        private int FieldRegion(Field f, float x, float y)
        {
            if (f.Tinctures.Count < 2) return 0;
            var line = f.Line;
            // Stripes and checks keep the real grid; partitions (and gyrons) meet at the centre point
            if (f.Partition != PartitionKind.None || f.Variation == VariationKind.Gyronny) { x = WX(x); y = WY(y); }

            switch (f.Partition)
            {
                case PartitionKind.Pale:
                    return x - w * 0.5f - Disp(line, y) < 0 ? 0 : 1;
                case PartitionKind.Fess:
                    return y - fh * 0.5f - Disp(line, x) < 0 ? 0 : 1;
                case PartitionKind.Bend:
                {
                    // Line dexter chief (0,0) to sinister base (w,h); first tincture above (towards sinister chief)
                    float d = (x * fh - y * w) / diag, s = (x * w + y * fh) / diag;
                    return d - Disp(line, s) > 0 ? 0 : 1;
                }
                case PartitionKind.BendSinister:
                {
                    float xm = w - x;
                    float d = (xm * fh - y * w) / diag, s = (xm * w + y * fh) / diag;
                    return d - Disp(line, s) > 0 ? 0 : 1;
                }
                case PartitionKind.Chevron:
                    return ChevronSide(x, y, 0f, line) < 0 ? 0 : 1;
                case PartitionKind.Saltire:
                {
                    float d1 = (x * fh - y * w) / diag, s1 = (x * w + y * fh) / diag;
                    float xm = w - x;
                    float d2 = (xm * fh - y * w) / diag, s2 = (xm * w + y * fh) / diag;
                    bool a = d1 - Disp(line, s1) > 0, b = d2 - Disp(line, s2) > 0;
                    return a == b ? 0 : 1; // chief and base vs the flanks
                }
                case PartitionKind.Quarterly:
                {
                    bool left = x - w * 0.5f - Disp(line, y) < 0;
                    bool top = y - fh * 0.5f - Disp(line, x) < 0;
                    return left == top ? 0 : 1; // 1st and 4th quarters take the first tincture
                }
            }

            int n = Math.Max(2, f.Count);
            switch (f.Variation)
            {
                case VariationKind.Paly:
                    return Parity((x - Disp(line, y)) / (w / (float)n));
                case VariationKind.Barry:
                    return Parity((y - Disp(line, x)) / (fh / (float)n));
                case VariationKind.Bendy:
                case VariationKind.BendySinister:
                {
                    float xx = f.Variation == VariationKind.Bendy ? x : w - x;
                    // Stripes parallel to the corner-to-corner bend, counted from sinister chief
                    float q = (xx * fh - y * w) / diag, s = (xx * w + y * fh) / diag;
                    float half = w * fh / diag;
                    return Parity((half - q + Disp(line, s)) / (2 * half / n));
                }
                case VariationKind.Chequy:
                {
                    float cell = w / 6f;
                    return (Floor(x / cell) + Floor(y / cell)) & 1;
                }
                case VariationKind.Lozengy:
                {
                    float lw = w / 4f, lh = lw * 1.5f;
                    float u = x / lw + y / lh, v = x / lw - y / lh;
                    return (Floor(u) + Floor(v)) & 1;
                }
                case VariationKind.Gyronny:
                {
                    // Angle in the banner's own (stretched) square, so 8 gyrons meet the corners
                    double ux = x / w - 0.5, uy = -(y / fh - 0.5);
                    double phi = Math.Atan2(uy, ux) - Math.PI / 2; // 0 = straight up, counting towards dexter
                    if (phi < 0) phi += 2 * Math.PI;
                    int k = (int)(phi / (2 * Math.PI / n));
                    return k & 1;
                }
            }
            return 0;
        }

        // ================================================================== ordinaries

        /// <param name="grow">
        ///     Pixels to widen the ordinary by (fimbriation), or narrow it by when negative (the inside of a voided one).
        /// </param>
        private bool InsideOrdinary(Ordinary o, float x, float y, float grow)
        {
            float full = BandWidth * unit * 0.5f;          // half widths
            float dim = DiminutiveWidth * unit * 0.5f;
            float hw0 = o.Diminutive ? dim : full;
            float hw = hw0 + grow;
            int n = Math.Max(1, o.Count);
            var line = o.Line;
            // Ordinaries through the middle go through the centre point; a chief and a bordure follow the edges
            if (o.Kind != OrdinaryKind.Chief && o.Kind != OrdinaryKind.Bordure) { x = WX(x); y = WY(y); }

            switch (o.Kind)
            {
                case OrdinaryKind.Chief:
                {
                    float edge = ChiefDepth * fh;
                    return y < edge + grow + Disp(line, x) && (grow >= 0 || (y > -grow && x > -grow && x < w + grow));
                }
                case OrdinaryKind.Fess:
                    for (int i = 0; i < n; i++)
                    {
                        float cy = n == 1 ? fh * 0.5f : fh * (i + 1f) / (n + 1f);
                        // Embattled fesses and bars are crenellated on the upper edge only
                        if (Band(y - cy, x, hw, line == LineKind.Embattled ? LineKind.Straight : line, line)) return true;
                    }
                    return false;
                case OrdinaryKind.Pale:
                    for (int i = 0; i < n; i++)
                    {
                        float cx = n == 1 ? w * 0.5f : w * (i + 1f) / (n + 1f);
                        if (Band(x - cx, y, hw, line, line)) return true;
                    }
                    return false;
                case OrdinaryKind.Bend:
                case OrdinaryKind.BendSinister:
                {
                    float xx = o.Kind == OrdinaryKind.Bend ? x : w - x;
                    float d = (xx * fh - y * w) / diag, s = (xx * w + y * fh) / diag;
                    for (int i = 0; i < n; i++)
                    {
                        float offset = (i - (n - 1) * 0.5f) * hw0 * 4.2f;
                        if (Band(d - offset, s, hw, line, line)) return true;
                    }
                    return false;
                }
                case OrdinaryKind.Cross:
                    return Band(x - w * 0.5f, y, hw, line, line) || Band(y - fh * 0.5f, x, hw, line, line);
                case OrdinaryKind.Saltire:
                {
                    float d1 = (x * fh - y * w) / diag, s1 = (x * w + y * fh) / diag;
                    float xm = w - x;
                    float d2 = (xm * fh - y * w) / diag, s2 = (xm * w + y * fh) / diag;
                    return Band(d1, s1, hw, line, line) || Band(d2, s2, hw, line, line);
                }
                case OrdinaryKind.Chevron:
                    for (int i = 0; i < n; i++)
                    {
                        float shift = n == 1 ? 0 : (i - (n - 1) * 0.5f) * ChevronVerticalStep(hw0 * 3.2f);
                        float d = ChevronSide(x, y, shift, LineKind.Straight);
                        if (Band(d, ChevronS(x, y), hw, line, line)) return true;
                    }
                    return false;
                case OrdinaryKind.Pile:
                    for (int i = 0; i < n; i++)
                    {
                        float top = w * (i + 1f) / (n + 1f);
                        float halfTop = n == 1 ? w * 0.3f : w * 0.45f / n;
                        float tipX = w * 0.5f + (top - w * 0.5f) * 0.3f, tipY = h * 0.88f;
                        if (InWedge(x, y, top - halfTop, top + halfTop, tipX, tipY, line, grow)) return true;
                    }
                    return false;
                case OrdinaryKind.Bordure:
                {
                    float bw = BordureWidth * unit;
                    int ix = Math.Min(w - 1, Math.Max(0, (int)x)), iy = Math.Min(h - 1, Math.Max(0, (int)y));
                    float m = edgeDistance[iy * w + ix];
                    // position along the edge, approximated by the angle around the centre
                    float s = (float)Math.Atan2(y - h * 0.5f, x - w * 0.5f) * (w + h) * 0.32f;
                    return m < bw + grow - Disp(line, s) && (grow >= 0 || m > -grow);
                }
                case OrdinaryKind.Pall:
                {
                    float cx = w * 0.5f, cy = fh * 0.42f;
                    return Segment(x, y, cx, cy, 0, 0, hw, line) || Segment(x, y, cx, cy, w, 0, hw, line) ||
                           (y >= cy && Band(x - cx, y, hw, line, line));
                }
            }
            return false;
        }

        /// <summary>A straight band: d = signed distance from its centre line, s = position along it.</summary>
        private bool Band(float d, float s, float halfWidth, LineKind lower, LineKind upper)
        {
            // Wavy, nebuly and dancetty bands keep their width: both edges move together.
            if (lower == upper && IsShifted(lower))
            {
                float dd = d - Disp(lower, s);
                return dd < halfWidth && dd > -halfWidth;
            }
            // Engrailed, invected, indented, embattled: each edge is pushed outward on its own.
            return d < halfWidth + Disp(lower, s) && d > -halfWidth - Disp(upper, s);
        }

        private static bool IsShifted(LineKind k) => k == LineKind.Wavy || k == LineKind.Nebuly || k == LineKind.Dancetty;

        private bool Segment(float x, float y, float ax, float ay, float bx, float by, float halfWidth, LineKind line)
        {
            float dx = bx - ax, dy = by - ay, len = (float)Math.Sqrt(dx * dx + dy * dy);
            dx /= len; dy /= len;
            float s = (x - ax) * dx + (y - ay) * dy;
            if (s < 0 || s > len) return false;
            float d = -(x - ax) * dy + (y - ay) * dx;
            return Band(d, s, halfWidth, line, line);
        }

        /// <summary>Signed distance from the chevron line (positive below it); legs run from the apex to the lower corners.</summary>
        private float ChevronSide(float x, float y, float shift, LineKind line)
        {
            float ax = w * 0.5f, ay = ChevronApex * fh + shift;
            float bx = x < ax ? 0 : w, by = fh + shift;
            float dx = bx - ax, dy = by - ay, len = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = -dy / len, ny = dx / len;
            if (ny < 0) { nx = -nx; ny = -ny; } // the normal that points down
            float d = (x - ax) * nx + (y - ay) * ny;
            return d - Disp(line, ChevronS(x, y));
        }

        /// <summary>Distance along a chevron leg from the apex, so line patterns follow the leg.</summary>
        private float ChevronS(float x, float y)
        {
            float ax = w * 0.5f, ay = ChevronApex * fh;
            float dx = ax, dy = fh - ay, len = (float)Math.Sqrt(dx * dx + dy * dy);
            return (Math.Abs(x - ax) * dx + (y - ay) * dy) / len;
        }

        /// <summary>How far the apex must move vertically for chevrons to sit a given distance apart.</summary>
        private float ChevronVerticalStep(float perpendicular)
        {
            float dx = w * 0.5f, dy = fh - ChevronApex * fh;
            float ny = dx / (float)Math.Sqrt(dx * dx + dy * dy);
            return perpendicular / Math.Max(0.2f, ny);
        }

        private bool InWedge(float x, float y, float x0, float x1, float tipX, float tipY, LineKind line, float grow)
        {
            if (y < (grow < 0 ? -grow : 0) || y > tipY + grow * 2.5f) return false;
            float t = y / tipY;
            float left = x0 + (tipX - x0) * t, right = x1 + (tipX - x1) * t;
            float d = Math.Min(x - left, right - x) * 0.96f; // ~perpendicular distance to the slanted sides
            return d + grow > -Disp(line, y);
        }

        /// <summary>
        ///     Distance (pixels) from each pixel to the nearest edge of the cloth: the outline mask if there is one,
        ///     else the rectangle. Two-pass chamfer transform; close enough for a bordure.
        /// </summary>
        private float[] DistanceToEdge()
        {
            const float Inf = 1e9f, A = 1f, D = 1.4142f;
            var dist = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool inside = outline == null || outline[y * w + x];
                // pixels outside the cloth, and the image border, are distance 0
                dist[y * w + x] = !inside ? 0 : Math.Min(Math.Min(x + 0.5f, w - x - 0.5f), Math.Min(y + 0.5f, h - y - 0.5f));
                if (inside && outline != null) dist[y * w + x] = Math.Min(dist[y * w + x], Inf);
            }
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = dist[y * w + x];
                if (x > 0) v = Math.Min(v, dist[y * w + x - 1] + A);
                if (y > 0)
                {
                    v = Math.Min(v, dist[(y - 1) * w + x] + A);
                    if (x > 0) v = Math.Min(v, dist[(y - 1) * w + x - 1] + D);
                    if (x < w - 1) v = Math.Min(v, dist[(y - 1) * w + x + 1] + D);
                }
                dist[y * w + x] = v;
            }
            for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                float v = dist[y * w + x];
                if (x < w - 1) v = Math.Min(v, dist[y * w + x + 1] + A);
                if (y < h - 1)
                {
                    v = Math.Min(v, dist[(y + 1) * w + x] + A);
                    if (x < w - 1) v = Math.Min(v, dist[(y + 1) * w + x + 1] + D);
                    if (x > 0) v = Math.Min(v, dist[(y + 1) * w + x - 1] + D);
                }
                dist[y * w + x] = v;
            }
            return dist;
        }

        // ================================================================== lines of partition

        /// <summary>
        ///     Outward displacement of an edge at position s along it, in pixels. Periods and depths are fixed
        ///     fractions of the narrow side, so a wavy line looks the same on any banner shape.
        /// </summary>
        private float Disp(LineKind kind, float s)
        {
            switch (kind)
            {
                case LineKind.Engrailed:
                {
                    float p = unit / 8f, a = p * 0.32f;
                    return a * (0.5f - Semicircle(Frac(s / p)));
                }
                case LineKind.Invected:
                {
                    float p = unit / 8f, a = p * 0.32f;
                    return -a * (0.5f - Semicircle(Frac(s / p)));
                }
                case LineKind.Indented:
                {
                    float p = unit / 8f, a = p * 0.55f;
                    return a * (Math.Abs(2 * Frac(s / p) - 1) - 0.5f);
                }
                case LineKind.Dancetty:
                {
                    float p = unit / 3f, a = p * 0.45f;
                    return a * (Math.Abs(2 * Frac(s / p) - 1) - 0.5f);
                }
                case LineKind.Wavy:
                {
                    float p = unit / 3f, a = p * 0.22f;
                    return a * 0.5f * (float)Math.Sin(2 * Math.PI * s / p);
                }
                case LineKind.Nebuly:
                {
                    // Approximation: asymmetric lobes. True nebuly overhangs, which this can't do yet.
                    float p = unit / 4f, a = p * 0.45f;
                    double th = 2 * Math.PI * s / p;
                    return a * 0.5f * (float)Math.Sin(th + 1.1 * Math.Sin(th));
                }
                case LineKind.Embattled:
                {
                    float p = unit / 6f, a = p * 0.45f;
                    return Frac(s / p) < 0.5f ? a * 0.5f : -a * 0.5f;
                }
            }
            return 0f;
        }

        private static float Semicircle(float f)
        {
            float t = 2 * f - 1;
            return (float)Math.Sqrt(Math.Max(0f, 1 - t * t));
        }

        private static float Frac(float v) => v - (float)Math.Floor(v);
        private static int Floor(float v) => (int)Math.Floor(v);
        private static int Parity(float v) => Floor(v) & 1;

        // ================================================================== tinctures and furs

        private Rgba Colour(Tincture t, float x, float y)
        {
            if (t == null) return Magenta;
            if (t.Kind == TinctureKind.Hex) return t.Hex;
            if (t.Kind == TinctureKind.Fur) return Fur(t.Name, x, y);
            if (t.Kind == TinctureKind.Tartan) return TartanAt(t.Sett, x, y);
            return Lexicon.Palette.TryGetValue(t.Name, out var c) ? c : Magenta;
        }

        /// <summary>Each tartan drawn once over the whole image, centred on the fess point, then looked up per sample.</summary>
        private readonly Dictionary<Sett, Rgba[]> tartans = new Dictionary<Sett, Rgba[]>();

        /// <summary>While finding edges: every tartan as its flat average colour.</summary>
        private bool flatTartans;

        private Rgba TartanAt(Sett sett, float x, float y)
        {
            if (sett == null || sett.Threads == null || sett.Threads.Length == 0) return Magenta;
            if (flatTartans) return sett.Average;
            if (!tartans.TryGetValue(sett, out var img))
            {
                img = Tartan.Draw(sett, w, h, cxp, cyp, unit / TartanThreadsAcross);
                tartans[sett] = img;
            }
            int ix = Math.Max(0, Math.Min(w - 1, (int)x)), iy = Math.Max(0, Math.Min(h - 1, (int)y));
            return img[iy * w + ix];
        }

        private Rgba Fur(string name, float x, float y)
        {
            Rgba P(string n) => Lexicon.Palette[n];
            switch (name)
            {
                case "ermine": return ErmineSpot(x, y) ? P("sable") : P("argent");
                case "ermines": return ErmineSpot(x, y) ? P("argent") : P("sable");
                case "erminois": return ErmineSpot(x, y) ? P("sable") : P("or");
                case "pean": return ErmineSpot(x, y) ? P("or") : P("sable");
                case "vair": return VairBell(x, y, false) ? P("azure") : P("argent");
                case "counter-vair": return VairBell(x, y, true) ? P("azure") : P("argent");
                case "potent": return Potent(x, y, false) ? P("azure") : P("argent");
                case "counter-potent": return Potent(x, y, true) ? P("azure") : P("argent");
            }
            return Magenta;
        }

        /// <summary>Ermine spots in a staggered grid: a tapering tail under three dots.</summary>
        private bool ErmineSpot(float x, float y)
        {
            float cw = unit / 4f, ch = cw * 1.15f;
            int row = Floor(y / ch);
            float ox = (row & 1) == 1 ? cw * 0.5f : 0f;
            float lx = Frac((x + ox) / cw) * cw - cw * 0.5f;
            float ly = Frac(y / ch) * ch - ch * 0.5f;
            float r = cw * 0.11f;

            // three dots
            if (Dot(lx, ly, 0, -r * 3.0f, r * 0.75f) || Dot(lx, ly, -r * 1.5f, -r * 2.1f, r * 0.75f) || Dot(lx, ly, r * 1.5f, -r * 2.1f, r * 0.75f))
                return true;
            // tail: widens downward from just under the dots, with a split foot
            if (ly > -r * 1.4f && ly < r * 2.8f)
            {
                float t = (ly + r * 1.4f) / (r * 4.2f);
                float half = r * (0.35f + 1.25f * t);
                if (Math.Abs(lx) < half)
                {
                    // two notches in the foot make the three points
                    if (t > 0.75f && Math.Abs(Math.Abs(lx) - half * 0.5f) < r * 0.25f * (t - 0.75f) / 0.25f * 1.4f) return false;
                    return true;
                }
            }
            return false;
        }

        private static bool Dot(float x, float y, float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) < r * r;

        /// <summary>Vair: rows of bell shapes, azure pointing up interlocked with argent pointing down.</summary>
        private bool VairBell(float x, float y, bool counter)
        {
            float cw = unit / 4f, ch = cw * 0.8f;
            int row = Floor(y / ch);
            float t = Frac(y / ch);
            bool flip = counter && (row & 1) == 1;
            float ox = (!counter && (row & 1) == 1) ? cw * 0.5f : 0f;
            if (flip) t = 1 - t;
            float lx = Frac((x + ox) / cw) - 0.5f; // -0.5..0.5 across one bell
            // half width of the bell at height t (0 = top of row, 1 = bottom)
            float hw = t < 0.35f ? 0.18f : t < 0.55f ? 0.18f + (t - 0.35f) / 0.2f * 0.14f : t < 0.7f ? 0.32f + (t - 0.55f) / 0.15f * 0.18f : 0.5f;
            return Math.Abs(lx) < hw;
        }

        /// <summary>
        ///     Potent: in each row an azure T (bar 3/4 wide on top, stem 1/4 below) interlocks with an argent
        ///     inverted T of the same shape. Rows are offset by half a cell; counter-potent mirrors alternate rows.
        /// </summary>
        private bool Potent(float x, float y, bool counter)
        {
            float cw = unit / 4f, ch = cw * 0.6f;
            int row = Floor(y / ch);
            float t = Frac(y / ch);
            bool odd = (row & 1) == 1;
            float ox = (!counter && odd) ? cw * 0.5f : 0f;
            if (counter && odd) t = 1 - t;
            float lx = Math.Abs(Frac((x + ox) / cw) - 0.5f); // 0 at the centre of the azure T
            return t < 0.5f ? lx < 0.375f : lx < 0.125f;
        }
    }
}
