using System;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     The geometric charges as signed distance functions: for a point (u, v) in the charge's own box
    ///     (-1..1 both ways, v pointing down), the distance to the shape's edge, negative inside.
    ///     A distance rather than an inside/outside test is what makes voided (a ring just inside the edge),
    ///     fimbriated (a ring just outside) and smooth edges cheap, for every shape at once.
    ///     Distances are exact for circles, boxes and polygons and close enough for the unions of them.
    /// </summary>
    public static class ChargeShapes
    {
        /// <summary>Width of a voided charge's rim and a fimbriated charge's edging, in box units.</summary>
        public const float RimWidth = 0.13f;

        public static float Distance(Charge c, float u, float v)
        {
            foreach (var a in c.Attitudes)
                if (a == "inverted") v = -v; // upside down (a crescent with its horns down)
                else if (a == "reversed") u = -u;

            switch (c.Noun)
            {
                case "roundel": case "bezant": case "plate": case "torteau": case "hurt":
                case "pellet": case "pomme": case "ogress": case "golpe":
                    return Circle(u, v, 0.94f);
                case "annulet":
                    return Math.Abs(Circle(u, v, 0.76f)) - 0.18f;
                case "lozenge":
                    return Rhombus(u, v, 0.64f, 0.96f);
                case "fusil":
                    return Rhombus(u, v, 0.40f, 0.96f);
                case "mascle":
                {
                    float d = Rhombus(u, v, 0.64f, 0.96f);
                    return Math.Abs(d + 0.13f) - 0.13f; // a lozenge voided: only the rim
                }
                case "rustre":
                    return Math.Max(Rhombus(u, v, 0.64f, 0.96f), -Circle(u, v, 0.22f));
                case "billet":
                    return Box(u, v, 0.52f, 0.94f);
                case "mullet":
                {
                    float d = Star(u, v + 0.06f, 0.98f, c.Points, 0.40f);
                    return c.Pierced ? Math.Max(d, -Circle(u, v + 0.06f, 0.2f)) : d;
                }
                case "crescent":
                    // Horns up: a disc with a smaller disc taken out of its top
                    return Math.Max(Circle(u, v - 0.12f, 0.78f), -Circle(u, v + 0.28f, 0.66f));
                case "cross": case "cross couped":
                    return Cross(u, v, 0.92f, 0.2f);
                case "saltorel": case "saltire":
                {
                    const float r = 0.70710678f;
                    return Cross((u + v) * r, (v - u) * r, 1.12f, 0.17f);
                }
                case "cross crosslet":
                {
                    float d = Cross(u, v, 0.92f, 0.12f);
                    // each arm crossed again two thirds of the way out
                    float au = Math.Abs(u), av = Math.Abs(v);
                    d = Math.Min(d, Box(au - 0.6f, v, 0.12f, 0.34f));
                    d = Math.Min(d, Box(u, av - 0.6f, 0.34f, 0.12f));
                    return d;
                }
                case "cross pattee":
                    return Pattee(u, v);
                case "cross potent":
                {
                    float d = Cross(u, v, 0.78f, 0.13f);
                    float au = Math.Abs(u), av = Math.Abs(v);
                    d = Math.Min(d, Box(au - 0.84f, v, 0.12f, 0.4f));
                    d = Math.Min(d, Box(u, av - 0.84f, 0.4f, 0.12f));
                    return d;
                }
            }
            return 1f; // not a drawn shape: nothing
        }

        // ---------------------------------------------------------------- primitives

        private static float Len(float x, float y) => (float)Math.Sqrt(x * x + y * y);

        private static float Circle(float x, float y, float r) => Len(x, y) - r;

        /// <summary>Axis-aligned box with half sizes hx, hy.</summary>
        private static float Box(float x, float y, float hx, float hy)
        {
            float dx = Math.Abs(x) - hx, dy = Math.Abs(y) - hy;
            return Len(Math.Max(dx, 0), Math.Max(dy, 0)) + Math.Min(Math.Max(dx, dy), 0);
        }

        /// <summary>Plain cross couped: two bars of half width hw reaching out to arm.</summary>
        private static float Cross(float x, float y, float arm, float hw) =>
            Math.Min(Box(x, y, arm, hw), Box(x, y, hw, arm));

        /// <summary>Diamond with half width a and half height b.</summary>
        private static float Rhombus(float x, float y, float a, float b)
        {
            // Distance to the edge from (a,0) to (0,b), folded into the first quadrant
            x = Math.Abs(x);
            y = Math.Abs(y);
            float ex = -a, ey = b, len2 = ex * ex + ey * ey;
            float t = Clamp01(((x - a) * ex + y * ey) / len2);
            float px = x - (a + ex * t), py = y - ey * t;
            float dist = Len(px, py);
            float side = x * b + y * a - a * b; // > 0 outside
            return side > 0 ? dist : -dist;
        }

        /// <summary>Regular star with n points: outer radius r, inner radius r * inner. First point straight up.</summary>
        private static float Star(float x, float y, float r, int n, float inner)
        {
            // Fold into one half-sector, then distance to the edge from the tip to the inner corner
            double sector = Math.PI / n;
            double a = Math.Atan2(x, -y); // 0 = straight up
            a = Math.Abs(a % (2 * sector));
            if (a > sector) a = 2 * sector - a;
            float rr = Len(x, y);
            float px = rr * (float)Math.Sin(a), py = rr * (float)Math.Cos(a); // tip on the +y axis
            float tx = 0, ty = r;
            float ix = r * inner * (float)Math.Sin(sector), iy = r * inner * (float)Math.Cos(sector);
            float ex = ix - tx, ey = iy - ty, len2 = ex * ex + ey * ey;
            float t = Clamp01(((px - tx) * ex + (py - ty) * ey) / len2);
            float dist = Len(px - (tx + ex * t), py - (ty + ey * t));
            float cross = ex * (py - ty) - ey * (px - tx); // which side of the edge
            return cross < 0 ? -dist : dist;
        }

        /// <summary>Cross pattée (formy): arms narrow at the centre and flaring to wide, straight ends.</summary>
        private static float Pattee(float x, float y)
        {
            // Fold so the point is in the upper arm's half, then it's one trapezoid's distance
            float ax = Math.Abs(x), ay = Math.Abs(y);
            if (ax > ay) { float t = ax; ax = ay; ay = t; }
            const float end = 0.95f, half = 0.46f, waist = 0.1f;
            // Trapezoid: (waist, waist) up to (half, end) on the outer side, capped at y = end
            float ex = half - waist, ey = end - waist, len = Len(ex, ey);
            float side = ((ax - waist) * ey - (ay - waist) * ex) / len; // > 0 outside the slanted edge
            float cap = ay - end;
            return Math.Max(side, cap);
        }

        private static float Clamp01(float t) => t < 0 ? 0 : t > 1 ? 1 : t;
    }
}
