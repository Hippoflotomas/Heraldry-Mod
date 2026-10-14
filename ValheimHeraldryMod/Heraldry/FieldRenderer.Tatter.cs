using System;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     "slightly tattered", "tattered", "badly tattered" (Hippo, 2026-10-14; this mod's own words): drawn into the
    ///     transparency, so the surface decides what a gap means.
    ///     <list type="bullet">
    ///         <item>Cloth (banners, tabards): a ragged bottom edge, nicked sides, rips up from the bottom and a few
    ///         holes; the top, where it hangs, stays whole. Gaps are see-through.</item>
    ///         <item>Paint (shields, laid out Compact): scuffed patches and chipped edges where the paint is gone,
    ///         so the shield's own wood or metal shows.</item>
    ///     </list>
    ///     The pattern comes from the blazon's own text (integer hashing), so everyone sees the same tatters.
    /// </summary>
    public sealed partial class FieldRenderer
    {
        private static readonly float[] BottomDepth = { 0f, 0.05f, 0.11f, 0.20f };
        private static readonly float[] SideDepth = { 0f, 0.015f, 0.035f, 0.06f };
        private static readonly int[] HoleCount = { 0, 1, 3, 6 };
        private static readonly int[] RipCount = { 0, 1, 2, 4 };
        private static readonly float[] WearAmount = { 1f, 0.80f, 0.70f, 0.60f };   // fbm above this = worn
        private static readonly float[] ChipDepth = { 0f, 0.012f, 0.025f, 0.045f };
        private static readonly Rgba Grime = new Rgba(46, 36, 26);

        private void Tatter(Rgba[] px)
        {
            int level = Math.Max(0, Math.Min(3, blazon.Tattered));
            if (level == 0) return;
            uint seed = Hash(blazon.Text ?? "");
            var gone = arrangement == Arrangement.Compact ? Wear(level, seed) : Tear(level, seed);

            // Fray and grime: what's left right beside a gap is darker
            int reach = Math.Max(1, (int)(unit / 160));
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (gone[i]) { px[i].A = 0; continue; }
                if (!InCloth(x, y)) continue;
                bool near = false;
                for (int dy = -reach; dy <= reach && !near; dy++)
                for (int dx = -reach; dx <= reach && !near; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && yy >= 0 && xx < w && yy < h && gone[yy * w + xx]) near = true;
                }
                if (near) px[i] = Mix(px[i], Grime, 0.35f);
            }
        }

        private bool InCloth(int x, int y) => outline == null || outline[y * w + x];

        /// <summary>Torn cloth: which pixels are gone.</summary>
        private bool[] Tear(int level, uint seed)
        {
            var gone = new bool[w * h];
            // The cloth's extent per column and row (the outline may be pointed or swallow-tailed)
            var bottom = new int[w];
            var left = new int[h];
            var right = new int[h];
            for (int x = 0; x < w; x++) { bottom[x] = -1; for (int y = h - 1; y >= 0; y--) if (InCloth(x, y)) { bottom[x] = y; break; } }
            for (int y = 0; y < h; y++)
            {
                left[y] = -1; right[y] = -1;
                for (int x = 0; x < w; x++) if (InCloth(x, y)) { if (left[y] < 0) left[y] = x; right[y] = x; }
            }

            var rng = new Rng(seed);
            int rips = RipCount[level];
            var ripX = new float[rips];
            var ripW = new float[rips];
            var ripLen = new float[rips];
            for (int r = 0; r < rips; r++)
            {
                ripX[r] = (0.12f + 0.76f * rng.Next01()) * w;
                ripW[r] = (0.012f + 0.02f * rng.Next01()) * w;
                ripLen[r] = (0.08f + 0.22f * rng.Next01()) * h * (0.6f + 0.2f * level);
            }
            int holes = HoleCount[level];
            var holeX = new float[holes];
            var holeY = new float[holes];
            var holeR = new float[holes];
            for (int k = 0; k < holes; k++)
            {
                holeX[k] = (0.15f + 0.7f * rng.Next01()) * w;
                holeY[k] = (0.35f + 0.55f * rng.Next01()) * h;
                holeR[k] = (0.015f + 0.035f * rng.Next01()) * w * (0.7f + 0.25f * level);
            }

            float bottomDepth = BottomDepth[level] * h, sideDepth = SideDepth[level] * w;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!InCloth(x, y)) continue;
                float fy = y / (float)h;
                // Ragged bottom
                float depth = bottomDepth * (0.3f + 0.7f * Fbm(x / (float)w * 7f, 0.5f, seed));
                bool cut = bottom[x] - y < depth;
                // Rips up from the bottom: narrowing slits that wander a little
                for (int r = 0; r < rips && !cut; r++)
                {
                    float up = bottom[x] - y;
                    if (up < 0 || up > ripLen[r]) continue;
                    float centre = ripX[r] + (Fbm(r * 3.1f, y / (float)h * 9f, seed) - 0.5f) * ripW[r] * 3f;
                    cut = Math.Abs(x - centre) < ripW[r] * (1f - up / ripLen[r]);
                }
                // Nicked sides, worse towards the bottom
                if (!cut && left[y] >= 0)
                {
                    float side = sideDepth * fy * fy * (0.2f + 1.3f * Fbm(fy * 11f, 2.7f, seed));
                    cut = x - left[y] < side || right[y] - x < side;
                }
                // Holes, irregular
                for (int k = 0; k < holes && !cut; k++)
                {
                    float dx = x - holeX[k], dy = y - holeY[k];
                    float r = holeR[k] * (0.65f + 0.7f * Fbm(x / (float)unit * 9f, y / (float)unit * 9f, seed + 17u));
                    cut = dx * dx + dy * dy < r * r;
                }
                gone[y * w + x] = cut;
            }
            return gone;
        }

        /// <summary>Worn paint: scuffed patches and chipped edges.</summary>
        private bool[] Wear(int level, uint seed)
        {
            var gone = new bool[w * h];
            var edge = DistanceToEdge();
            float threshold = WearAmount[level], chip = ChipDepth[level] * unit;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!InCloth(x, y)) continue;
                float n = Fbm(x / (float)unit * 5f, y / (float)unit * 5f, seed);
                bool worn = n > threshold;
                if (!worn && edge != null)
                    worn = edge[y * w + x] < chip * Math.Max(0f, (Fbm(x / (float)unit * 9f, y / (float)unit * 9f, seed + 5u) - 0.45f) * 4f); // chips, not a ring
                gone[y * w + x] = worn;
            }
            return gone;
        }

        // ------------------------------------------------------------------ deterministic noise

        private static uint Hash(string s)
        {
            uint h = 2166136261u; // FNV-1a over the folded text
            foreach (char c in s.ToLowerInvariant()) { h ^= c; h *= 16777619u; }
            return h == 0 ? 1u : h;
        }

        private static uint Mix32(uint a)
        {
            a ^= a >> 16; a *= 0x7feb352du; a ^= a >> 15; a *= 0x846ca68bu; a ^= a >> 16;
            return a;
        }

        private static float Lattice(int x, int y, uint seed) =>
            (Mix32((uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u ^ seed) & 0xFFFFFF) / (float)0xFFFFFF;

        /// <summary>Smooth value noise, 0..1.</summary>
        private static float ValueNoise(float x, float y, uint seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3 - 2 * tx);
            ty = ty * ty * (3 - 2 * ty);
            float a = Lattice(x0, y0, seed), b = Lattice(x0 + 1, y0, seed);
            float c = Lattice(x0, y0 + 1, seed), d = Lattice(x0 + 1, y0 + 1, seed);
            float top = a + (b - a) * tx, bot = c + (d - c) * tx;
            return top + (bot - top) * ty;
        }

        /// <summary>Three octaves of value noise, 0..1.</summary>
        private static float Fbm(float x, float y, uint seed)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int o = 0; o < 3; o++)
            {
                sum += amp * ValueNoise(x, y, seed + (uint)o * 101u);
                norm += amp;
                x *= 2.03f; y *= 2.03f; amp *= 0.5f;
            }
            return sum / norm;
        }

        private struct Rng
        {
            private uint state;
            public Rng(uint seed) { state = seed == 0 ? 1u : seed; }
            public float Next01()
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                return (state & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
