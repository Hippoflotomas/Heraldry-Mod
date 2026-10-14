using System;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     Rasterises the cloth mesh's triangles (in its 0..1 texture space) into a "this pixel is cloth" mask,
    ///     so ordinaries that hug the edge (the bordure) follow the real outline, e.g. the vanilla banner's point.
    /// </summary>
    public static class OutlineMask
    {
        /// <param name="uv">x,y pairs in 0..1, V pointing up (Unity convention).</param>
        /// <param name="triangles">vertex indices, three per triangle.</param>
        /// <param name="hiddenTop">Fraction of the height, from the top, that can't be seen (behind the beam).</param>
        /// <returns>w*h flags, rows top to bottom; null if the mesh covers (almost) the whole rectangle.</returns>
        public static bool[] FromTriangles(float[] uv, int[] triangles, int w, int h, float hiddenTop = 0f)
        {
            if (uv == null || triangles == null || triangles.Length < 3) return null;
            var mask = new bool[w * h];
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                float x0 = uv[triangles[t] * 2] * w, y0 = (1 - uv[triangles[t] * 2 + 1]) * h;
                float x1 = uv[triangles[t + 1] * 2] * w, y1 = (1 - uv[triangles[t + 1] * 2 + 1]) * h;
                float x2 = uv[triangles[t + 2] * 2] * w, y2 = (1 - uv[triangles[t + 2] * 2 + 1]) * h;
                float area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
                if (Math.Abs(area) < 1e-6f) continue;
                int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))) - 1);
                int maxX = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))) + 1);
                int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))) - 1);
                int maxY = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))) + 1);
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float a = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) / area;
                    float b = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) / area;
                    float c = 1 - a - b;
                    const float e = -0.02f; // tiny overlap so shared edges leave no gaps
                    if (a >= e && b >= e && c >= e) mask[y * w + x] = true;
                }
            }
            FillTopNotches(mask, w, h);
            HideTop(mask, w, h, hiddenTop);
            int covered = 0;
            foreach (bool m in mask) if (m) covered++;
            return covered > w * h * 0.995f ? null : mask;
        }

        /// <summary>
        ///     The vanilla banner's top edge has gaps between its hanging loops. Treat everything above the first
        ///     full-width row as cloth, so a bordure runs straight along the top instead of dipping into each gap
        ///     and the editor's preview shows a clean edge. (The real cloth still has its gaps; nothing is painted
        ///     there that anyone can see.)
        /// </summary>
        /// <summary>
        ///     Marks the rows behind the beam as not cloth. The renderer lays the arms out below them and fills
        ///     them with the colour of the top visible row.
        /// </summary>
        public static void HideTop(bool[] mask, int w, int h, float hiddenTop)
        {
            int rows = (int)Math.Round(Math.Max(0f, Math.Min(0.3f, hiddenTop)) * h);
            for (int i = 0; i < rows * w; i++) mask[i] = false;
        }

        public static void FillTopNotches(bool[] mask, int w, int h)
        {
            for (int y = 0; y < h / 4; y++)
            {
                int covered = 0;
                for (int x = 0; x < w; x++) if (mask[y * w + x]) covered++;
                if (covered < w * 0.98f) continue;
                for (int yy = 0; yy < y; yy++)
                    for (int x = 0; x < w; x++)
                        mask[yy * w + x] = true;
                return;
            }
        }
    }
}
