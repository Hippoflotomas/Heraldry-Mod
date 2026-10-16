using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>One stripe of a threadcount: a colour code and its number of threads.</summary>
    public sealed class Stripe
    {
        public string Code;   // upper case ("DB")
        public int Threads;   // as written
        public Rgba Colour;
        /// <summary>Written with a slash ("B/24"): a half-count pivot, so the stripe is twice as wide at the pivot.</summary>
        public bool HalfPivot;
    }

    /// <summary>
    ///     A tartan's sett, read from a traditional threadcount (Hippo, 2026-10-15: traditional notation, 2/2 twill).
    ///     Conventions (Wikipedia "Tartan", Scottish Register of Tartans):
    ///     <list type="bullet">
    ///         <item>Without "...", the sett is symmetric: the first and last stripes are pivots and the count mirrors
    ///         about them. Written plainly ("B24 ... K4") the pivots are full-count (each pivot stripe appears once at
    ///         that width); with a slash ("B/24") the count is half the pivot stripe.</item>
    ///         <item>"...K4 R24 K24 Y4..." is an asymmetric sett, repeating without mirroring (rare).</item>
    ///     </list>
    ///     Warp and weft share the same count. The sett is centred on the field's centre: the first pivot stripe is
    ///     centred there and the pattern mirrors outward (Hippo, 2026-10-15).
    /// </summary>
    public sealed class Sett
    {
        /// <summary>Canonical threadcount text ("B24 K4 B4"), for naming and "of the first".</summary>
        public string Canonical;
        public List<Stripe> Stripes = new List<Stripe>();
        public bool Repeating;
        /// <summary>One full repeat, one colour per thread, starting at the first stripe's first thread.</summary>
        public Rgba[] Threads;
        /// <summary>Threads from the start of <see cref="Threads"/> to the centre of the first stripe.</summary>
        public float CentreOffset;
        /// <summary>Thread-weighted mean colour (what the cloth looks like from afar).</summary>
        public Rgba Average;
    }

    public static class Tartan
    {
        public const int MaxStripes = 64;
        public const int MaxThreadsPerStripe = 512;
        public const int MaxSettThreads = 4096;

        /// <summary>
        ///     Colour codes (tartandictionary.org's code list: six core colours, L/D for light and dark, plus orange,
        ///     purple, grey and brown). Values are shades from the Scottish Register of Tartans' palette, one picked
        ///     per code (Claude's pick, 2026-10-15; tune freely).
        /// </summary>
        public static readonly Dictionary<string, KeyValuePair<string, Rgba>> Codes = new Dictionary<string, KeyValuePair<string, Rgba>>
        {
            { "LR", C("light red", 0xE87878) }, { "R", C("red", 0xC80000) }, { "DR", C("dark red", 0x880000) },
            { "O", C("orange", 0xE86000) }, { "DO", C("dark orange", 0xBE7832) },
            { "LY", C("light yellow", 0xF8E38C) }, { "Y", C("yellow", 0xE8C000) }, { "DY", C("dark yellow", 0xBC8C00) },
            { "LG", C("light green", 0x86C67C) }, { "G", C("green", 0x006818) }, { "DG", C("dark green", 0x004028) },
            { "LB", C("light blue", 0x98C8E8) }, { "B", C("blue", 0x2C4084) }, { "DB", C("dark blue", 0x202060) },
            { "LP", C("light purple", 0xC49CD8) }, { "P", C("purple", 0x780078) }, { "DP", C("dark purple", 0x440044) },
            { "W", C("white", 0xF8F8F8) }, { "LN", C("light grey", 0xE0E0E0) }, { "N", C("grey", 0x888888) },
            { "DN", C("dark grey", 0x505050) }, { "K", C("black", 0x101010) },
            { "LT", C("light brown", 0xA08858) }, { "T", C("brown", 0x604000) }, { "DT", C("dark brown", 0x4C3428) },
        };

        private static KeyValuePair<string, Rgba> C(string name, int rgb) =>
            new KeyValuePair<string, Rgba>(name, new Rgba((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

        private const string CodeHelp = "codes: K W R B G Y O P N T, with L (light) or D (dark) in front, e.g. DB";

        /// <summary>
        ///     Reads the text inside the quotes. <paramref name="start"/> is its index in the blazon, for the notes
        ///     (one per stripe, with its colour for the editor's swatch). Returns null when nothing usable is left;
        ///     <paramref name="problem"/> then says why (or carries a note for a sett that still draws).
        /// </summary>
        public static Sett Parse(string inner, int start, List<WordNote> notes, out string problem)
        {
            problem = null;
            var sett = new Sett();
            string body = inner;
            int offset = start;

            // "...K4 R24 K24 Y4...": asymmetric (repeating). Dots at one end only are a typo for both.
            string trimmed = body.Trim();
            bool lead = trimmed.StartsWith("..") || trimmed.StartsWith("…");
            bool trail = trimmed.EndsWith("..") || trimmed.EndsWith("…");
            sett.Repeating = lead || trail;

            int i = 0;
            int pivotSlashes = 0;
            var entries = new List<KeyValuePair<Stripe, WordNote>>();
            while (i < body.Length)
            {
                char c = body[i];
                if (char.IsWhiteSpace(c) || c == ',' || c == '.' || c == '…') { i++; continue; }
                int s = i;
                while (i < body.Length && !char.IsWhiteSpace(body[i]) && body[i] != ',' && body[i] != '.' && body[i] != '…') i++;
                string word = body.Substring(s, i - s);
                var note = new WordNote { Start = offset + s, Length = i - s, Word = word, Status = WordStatus.Correct };
                notes.Add(note);
                var stripe = ReadEntry(word, note);
                if (stripe != null)
                {
                    entries.Add(new KeyValuePair<Stripe, WordNote>(stripe, note));
                    if (stripe.HalfPivot) pivotSlashes++;
                }
            }

            if (entries.Count == 0)
            {
                problem = "the threadcount is empty: colour codes and thread numbers, e.g. \"K4 R24 K24 Y4\"";
                return null;
            }
            if (entries.Count > MaxStripes)
            {
                problem = $"at most {MaxStripes} stripes in a threadcount";
                return null;
            }

            // A slash marks a pivot, so it only belongs on the first or last stripe of a symmetric count
            for (int k = 0; k < entries.Count; k++)
            {
                var st = entries[k].Key;
                if (!st.HalfPivot) continue;
                if (sett.Repeating)
                {
                    Flag(entries[k].Value, WordStatus.Wrong, "a repeating (\"...\") sett has no pivots; drop the /");
                    st.HalfPivot = false;
                }
                else if (k != 0 && k != entries.Count - 1)
                {
                    Flag(entries[k].Value, WordStatus.Wrong, "a pivot (/) only goes on the first or last stripe");
                    st.HalfPivot = false;
                }
            }

            foreach (var e in entries) sett.Stripes.Add(e.Key);
            if (!Build(sett))
            {
                problem = $"the sett is too big (over {MaxSettThreads} threads)";
                return null;
            }
            var sb = new StringBuilder();
            if (sett.Repeating) sb.Append("...");
            for (int k = 0; k < sett.Stripes.Count; k++)
            {
                var st = sett.Stripes[k];
                if (k > 0) sb.Append(' ');
                sb.Append(st.Code).Append(st.HalfPivot ? "/" : "").Append(st.Threads.ToString(CultureInfo.InvariantCulture));
            }
            if (sett.Repeating) sb.Append("...");
            sett.Canonical = sb.ToString();
            if (sett.Repeating && !(lead && trail))
                problem = "a repeating sett is written with \"...\" at both ends";
            return sett;
        }

        /// <summary>"B24", "db4", "K/2". Null (and a marked note) if it isn't one.</summary>
        private static Stripe ReadEntry(string word, WordNote note)
        {
            int j = 0;
            while (j < word.Length && char.IsLetter(word[j])) j++;
            string code = word.Substring(0, j).ToUpperInvariant();
            bool slash = j < word.Length && word[j] == '/';
            int k = slash ? j + 1 : j;
            string digits = word.Substring(k);

            if (code.Length == 0)
            {
                if (word.StartsWith("#"))
                    Flag(note, WordStatus.Unsupported, "hex colours inside a threadcount aren't supported yet; use a colour code");
                else
                    Flag(note, WordStatus.Wrong, "expected a colour code and a number of threads, e.g. B24");
                return null;
            }
            // "W/Y24" (two colours sharing a count) is an old shorthand; not read
            if (slash && digits.Length > 0 && char.IsLetter(digits[0]))
            {
                Flag(note, WordStatus.Unsupported, "the W/Y shorthand isn't supported; write each stripe out, e.g. W24 Y24");
                return null;
            }
            if (!Codes.TryGetValue(code, out var entry))
            {
                Flag(note, WordStatus.Wrong, $"unknown colour code '{code}'; {CodeHelp}");
                return null;
            }
            if (digits.Length == 0)
            {
                Flag(note, WordStatus.Wrong, $"{entry.Key} needs a number of threads straight after it, e.g. {code}24");
                return null;
            }
            if (!IsDigits(digits))
            {
                Flag(note, WordStatus.Wrong, "expected a colour code and a number of threads, e.g. B24 or B/24 for a pivot");
                return null;
            }
            int n = int.Parse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (n < 1 || n > MaxThreadsPerStripe)
            {
                Flag(note, WordStatus.Wrong, $"between 1 and {MaxThreadsPerStripe} threads");
                return null;
            }
            note.Tincture = new Tincture { Kind = TinctureKind.Hex, Name = code, Hex = entry.Value };
            note.Message = $"{entry.Key}, {n} threads" + (slash ? " (half-count pivot: the stripe is twice that at the pivot)" : "");
            return new Stripe { Code = code, Threads = n, Colour = entry.Value, HalfPivot = slash };
        }

        private static void Flag(WordNote note, WordStatus s, string msg)
        {
            note.Status = s;
            note.Message = msg;
            note.Tincture = null;
        }

        /// <summary>Expands the stripes into one repeat of threads. False if it's too big.</summary>
        private static bool Build(Sett sett)
        {
            var st = sett.Stripes;
            var seq = new List<KeyValuePair<Rgba, int>>();
            if (sett.Repeating || st.Count == 1)
            {
                foreach (var s in st) seq.Add(new KeyValuePair<Rgba, int>(s.Colour, s.Threads));
                sett.CentreOffset = st[0].Threads * 0.5f;
            }
            else
            {
                // Symmetric: pivot, the middle stripes, the other pivot, the middle stripes backwards; then again
                int last = st.Count - 1;
                int p0 = st[0].HalfPivot ? st[0].Threads * 2 : st[0].Threads;
                int p1 = st[last].HalfPivot ? st[last].Threads * 2 : st[last].Threads;
                seq.Add(new KeyValuePair<Rgba, int>(st[0].Colour, p0));
                for (int k = 1; k < last; k++) seq.Add(new KeyValuePair<Rgba, int>(st[k].Colour, st[k].Threads));
                seq.Add(new KeyValuePair<Rgba, int>(st[last].Colour, p1));
                for (int k = last - 1; k >= 1; k--) seq.Add(new KeyValuePair<Rgba, int>(st[k].Colour, st[k].Threads));
                sett.CentreOffset = p0 * 0.5f;
            }

            int total = 0;
            foreach (var p in seq) total += p.Value;
            if (total > MaxSettThreads) return false;
            sett.Threads = new Rgba[total];
            int t = 0;
            long r = 0, g = 0, b = 0;
            foreach (var p in seq)
                for (int k = 0; k < p.Value; k++)
                {
                    sett.Threads[t++] = p.Key;
                    r += p.Key.R; g += p.Key.G; b += p.Key.B;
                }
            sett.Average = new Rgba((byte)(r / total), (byte)(g / total), (byte)(b / total));
            return true;
        }

        private static bool IsDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>
        ///     Draws the cloth into a whole image: <paramref name="threadPx"/> pixels per thread, centred on
        ///     (<paramref name="cx"/>, <paramref name="cy"/>). A 2/2 twill: at the crossing of warp thread i (a column)
        ///     and weft thread j (a row) the warp shows when (i + j) mod 4 is 0 or 1, so each thread goes over two and
        ///     under two, stepping one along per row, which makes the diagonal ribs and blends the colours where
        ///     stripes cross. Each pixel is the exact average of the thread crossings it covers (a box filter), so
        ///     threads finer than a pixel blend instead of shimmering.
        /// </summary>
        public static Rgba[] Draw(Sett sett, int w, int h, float cx, float cy, float threadPx)
        {
            var px = new Rgba[w * h];
            int n = sett.Threads.Length;
            float inv = 1f / threadPx;
            // Thread coordinates of every pixel edge, per axis
            var xs = new float[w + 1];
            var ys = new float[h + 1];
            for (int x = 0; x <= w; x++) xs[x] = (x - cx) * inv + sett.CentreOffset;
            for (int y = 0; y <= h; y++) ys[y] = (y - cy) * inv + sett.CentreOffset;

            var wi = new List<int>(); var ww = new List<float>();
            var wj = new List<int>(); var wwj = new List<float>();
            for (int y = 0; y < h; y++)
            {
                Cover(ys[y], ys[y + 1], wj, wwj);
                for (int x = 0; x < w; x++)
                {
                    Cover(xs[x], xs[x + 1], wi, ww);
                    float r = 0, g = 0, b = 0, sum = 0;
                    for (int a = 0; a < wi.Count; a++)
                    {
                        int i = wi[a];
                        var warp = sett.Threads[Mod(i, n)];
                        for (int c = 0; c < wj.Count; c++)
                        {
                            int j = wj[c];
                            float k = ww[a] * wwj[c];
                            var col = Mod(i + j, 4) < 2 ? warp : sett.Threads[Mod(j, n)];
                            r += col.R * k; g += col.G * k; b += col.B * k; sum += k;
                        }
                    }
                    px[y * w + x] = new Rgba((byte)(r / sum + 0.5f), (byte)(g / sum + 0.5f), (byte)(b / sum + 0.5f));
                }
            }
            return px;
        }

        /// <summary>The threads overlapping [a, b) and how much of each lies inside.</summary>
        private static void Cover(float a, float b, List<int> idx, List<float> wt)
        {
            idx.Clear(); wt.Clear();
            int first = (int)Math.Floor(a), last = (int)Math.Floor(b - 1e-6f);
            if (last < first) last = first;
            for (int i = first; i <= last; i++)
            {
                float lo = Math.Max(a, i), hi = Math.Min(b, i + 1);
                if (hi > lo) { idx.Add(i); wt.Add(hi - lo); }
            }
            if (idx.Count == 0) { idx.Add(first); wt.Add(1f); }
        }

        private static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }
    }
}
